using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Notifications.Telegram;

public interface ITelegramPollingService
{
    long CurrentOffset { get; set; }
    Task<int> PollOnceAsync(CancellationToken cancellationToken = default);
}

public class TelegramPollingService : BackgroundService, ITelegramPollingService
{
    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = false,
    });

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IConfigService _configService;
    private readonly ITelegramUpdateHandler _updateHandler;
    private readonly TelegramSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ITelegramBotLifecycle _botLifecycle;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private long _offset;
    private bool _webhookCleanedUp;

    public TelegramPollingService(
        IConfigService configService,
        ITelegramUpdateHandler updateHandler,
        HttpClient httpClient = null,
        TelegramSettings settings = null,
        ITelegramBotLifecycle botLifecycle = null)
    {
        _configService = configService;
        _updateHandler = updateHandler;
        _httpClient = httpClient ?? SharedHttpClient;
        _settings = settings ?? new TelegramSettings();
        _botLifecycle = botLifecycle ?? new TelegramBotLifecycle(_configService, _httpClient, _settings);
        _logger = LogManager.GetCurrentClassLogger();
    }

    public long CurrentOffset
    {
        get => Interlocked.Read(ref _offset);
        set => Interlocked.Exchange(ref _offset, value);
    }

    private string GetBotToken()
    {
        return !string.IsNullOrWhiteSpace(_settings?.BotToken) ? _settings.BotToken : _configService?.TelegramBotToken;
    }

    public async Task<int> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var botToken = GetBotToken();
            if (string.IsNullOrWhiteSpace(botToken))
            {
                return 0;
            }

            var offset = CurrentOffset;
            var url = $"https://api.telegram.org/bot{botToken}/getUpdates?offset={offset}&timeout=30";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode == 429 || response.StatusCode == (HttpStatusCode)429)
            {
                throw new HttpRequestException("Telegram API rate limit reached (HTTP 429)", null, HttpStatusCode.TooManyRequests);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Telegram getUpdates failed with status {0}", response.StatusCode);
                throw new HttpRequestException($"Telegram getUpdates failed with status {response.StatusCode}", null, response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var apiResult = JsonSerializer.Deserialize<TelegramApiResponse<List<TelegramUpdate>>>(json, JsonOptions);

            if (apiResult?.Result == null || apiResult.Result.Count == 0)
            {
                return 0;
            }

            var processedCount = 0;
            var orderedUpdates = apiResult.Result.OrderBy(u => u.UpdateId).ToList();

            foreach (var update in orderedUpdates)
            {
                if (update.UpdateId < CurrentOffset)
                {
                    _logger.Debug("Skipping already processed update {0}", update.UpdateId);
                    continue;
                }

                CurrentOffset = update.UpdateId + 1;
                await _updateHandler.HandleUpdateAsync(update, cancellationToken).ConfigureAwait(false);
                processedCount++;
            }

            return processedCount;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Telegram polling worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_configService.TelegramUsePolling)
                {
                    _webhookCleanedUp = false;
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(GetBotToken()))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (!_webhookCleanedUp && _botLifecycle != null)
                {
                    try
                    {
                        await _botLifecycle.DeleteWebhookAsync(dropPendingUpdates: false, stoppingToken).ConfigureAwait(false);
                        _webhookCleanedUp = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to ensure Telegram webhook deleted prior to polling");
                    }
                }

                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException ex) when ((int?)ex.StatusCode == 429 || ex.Message.Contains("429"))
            {
                _logger.Warn("Telegram API rate limit (HTTP 429) encountered. Backing off for 30s.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    _webhookCleanedUp = false;
                }

                _logger.Warn("Telegram getUpdates request failed with status {0}. Backing off for 5s.", ex.StatusCode);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while polling Telegram updates. Backing off for 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.Info("Telegram polling worker stopped.");
    }

    public override void Dispose()
    {
        base.Dispose();
        _semaphore.Dispose();
    }
}
