using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Peers.Encryption;

public interface IDhKeyPool
{
    MseKeyDerivation Rent();
    int AvailableCount { get; }
}

public class DhKeyPool : BackgroundService, IDhKeyPool
{
    public const int DefaultTargetPoolSize = 32;

    private readonly ConcurrentBag<MseKeyDerivation> _pool = new();
    private readonly SemaphoreSlim _replenishSignal = new(0, 1);
    private readonly int _targetPoolSize;
    private readonly Logger _logger;

    public int AvailableCount => _pool.Count;
    public int TargetPoolSize => _targetPoolSize;

    public DhKeyPool()
        : this(DefaultTargetPoolSize)
    {
    }

    public DhKeyPool(int targetPoolSize)
    {
        _targetPoolSize = targetPoolSize > 0 ? targetPoolSize : DefaultTargetPoolSize;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public MseKeyDerivation Rent()
    {
        if (_pool.TryTake(out var key))
        {
            if (_pool.Count < _targetPoolSize)
            {
                SignalReplenishment();
            }

            return key;
        }

        // Fast fallback if pool drained
        _logger.Debug("DH key pool empty, generating key on-demand");
        SignalReplenishment();
        return new MseKeyDerivation();
    }

    public async Task ReplenishAsync(CancellationToken stoppingToken = default)
    {
        while (_pool.Count < _targetPoolSize && !stoppingToken.IsCancellationRequested)
        {
            _pool.Add(new MseKeyDerivation());
            await Task.Yield();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Debug("Diffie-Hellman ephemeral keypool service started (target size: {0})", _targetPoolSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReplenishAsync(stoppingToken);

                await _replenishSignal.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error replenishing Diffie-Hellman key pool");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public override void Dispose()
    {
        _replenishSignal.Dispose();
        base.Dispose();
    }

    private void SignalReplenishment()
    {
        try
        {
            if (_replenishSignal.CurrentCount == 0)
            {
                _replenishSignal.Release();
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.Trace(ex, "DhKeyPool replenish signal semaphore disposed");
        }
        catch (SemaphoreFullException ex)
        {
            _logger.Trace(ex, "DhKeyPool replenish signal semaphore full");
        }
    }
}
