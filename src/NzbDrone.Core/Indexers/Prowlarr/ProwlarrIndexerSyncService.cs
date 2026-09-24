using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Indexers.Prowlarr;

public class ProwlarrIndexerSyncService : IProwlarrIndexerSyncService
{
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly IIndexerRepository _indexerRepository;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public ProwlarrIndexerSyncService(
        IIndexerRepository indexerRepository,
        HttpClient httpClient = null,
        Logger logger = null)
    {
        _indexerRepository = indexerRepository;
        _httpClient = httpClient ?? DefaultClient;
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public ProwlarrSyncResult Sync(int? prowlarrIndexerDefinitionId = null, string baseUrl = null, string apiKey = null)
    {
        return SyncAsync(prowlarrIndexerDefinitionId, baseUrl, apiKey, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<ProwlarrSyncResult> SyncAsync(
        int? prowlarrIndexerDefinitionId = null,
        string baseUrl = null,
        string apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ProwlarrSyncResult();
        var prowlarrInstances = ResolveProwlarrInstances(prowlarrIndexerDefinitionId, baseUrl, apiKey, result);

        if (prowlarrInstances == null || prowlarrInstances.Count == 0)
        {
            if (result.Errors.Count == 0)
            {
                result.Success = false;
                result.Message = "No Prowlarr indexer configured for synchronization.";
                result.Errors.Add("No Prowlarr indexer found.");
            }

            return result;
        }

        foreach (var prowlarrDef in prowlarrInstances)
        {
            try
            {
                await SyncSingleInstanceAsync(prowlarrDef, result, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to synchronize Prowlarr indexers for {0} ({1})", prowlarrDef.Name, prowlarrDef.Url);
                result.Errors.Add($"Error syncing {prowlarrDef.Name}: {ex.Message}");
            }
        }

        result.Success = result.Errors.Count == 0;
        result.Message = result.Success
            ? $"Prowlarr sync complete: {result.Added} added, {result.Updated} updated, {result.Removed} removed."
            : $"Prowlarr sync completed with {result.Errors.Count} error(s): {result.Added} added, {result.Updated} updated, {result.Removed} removed.";

        return result;
    }

    private List<IndexerDefinition> ResolveProwlarrInstances(
        int? prowlarrIndexerDefinitionId,
        string baseUrl,
        string apiKey,
        ProwlarrSyncResult result)
    {
        if (prowlarrIndexerDefinitionId.HasValue)
        {
            var target = _indexerRepository.Get(prowlarrIndexerDefinitionId.Value);
            if (target == null)
            {
                result.Success = false;
                result.Message = $"Prowlarr indexer definition with ID {prowlarrIndexerDefinitionId.Value} not found.";
                result.Errors.Add("Indexer definition not found.");
                return null;
            }

            if (!string.Equals(target.IndexerType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = $"Indexer with ID {prowlarrIndexerDefinitionId.Value} is '{target.IndexerType}', expected 'Prowlarr'.";
                result.Errors.Add("Target indexer is not a Prowlarr instance.");
                return null;
            }

            return new List<IndexerDefinition> { target };
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return new List<IndexerDefinition>
            {
                new()
                {
                    Id = 0,
                    Name = "Prowlarr",
                    IndexerType = "Prowlarr",
                    Url = baseUrl,
                    ApiKey = apiKey,
                    Enable = true
                }
            };
        }

        var allIndexers = _indexerRepository.All();
        var prowlarrDefinitions = allIndexers
            .Where(i => string.Equals(i.IndexerType, "Prowlarr", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (prowlarrDefinitions.Count == 0)
        {
            return null;
        }

        var enabled = prowlarrDefinitions.Where(i => i.Enable).ToList();
        return enabled.Count > 0 ? enabled : prowlarrDefinitions;
    }

    private async Task SyncSingleInstanceAsync(
        IndexerDefinition prowlarrDef,
        ProwlarrSyncResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prowlarrDef.Url))
        {
            result.Errors.Add($"Prowlarr instance '{prowlarrDef.Name}' has no URL configured.");
            return;
        }

        var hostUrl = prowlarrDef.Url.TrimEnd('/');
        var requestUrl = $"{hostUrl}/api/v1/indexer";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        if (!string.IsNullOrWhiteSpace(prowlarrDef.ApiKey))
        {
            request.Headers.Add("X-Api-Key", prowlarrDef.ApiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var msg = $"Prowlarr at {hostUrl} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
            _logger.Warn(msg);
            result.Errors.Add(msg);
            return;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            result.Errors.Add($"Prowlarr response from {hostUrl} was not a JSON array.");
            return;
        }

        var allIndexers = _indexerRepository.All();
        var localSyncedIndexers = allIndexers.Where(i =>
            string.Equals(i.IndexerType, "Torznab", StringComparison.OrdinalIgnoreCase) &&
            ProwlarrSyncMetadata.IsProwlarrSyncedIndexer(i) &&
            (prowlarrDef.Id == 0 ||
             ProwlarrSyncMetadata.GetProwlarrInstanceId(i) == prowlarrDef.Id ||
             (!ProwlarrSyncMetadata.GetProwlarrInstanceId(i).HasValue && i.Url != null && i.Url.StartsWith(hostUrl, StringComparison.OrdinalIgnoreCase)))).ToList();

        var activeProwlarrIds = new HashSet<int>();

        foreach (var indexerElem in doc.RootElement.EnumerateArray())
        {
            if (!indexerElem.TryGetProperty("protocol", out var protocolProp) ||
                !string.Equals(protocolProp.GetString(), "torrent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!indexerElem.TryGetProperty("enable", out var enableProp))
            {
                continue;
            }

            var isEnabled = enableProp.ValueKind == JsonValueKind.True ||
                            (enableProp.ValueKind == JsonValueKind.String &&
                             bool.TryParse(enableProp.GetString(), out var parsedBool) && parsedBool);

            if (!isEnabled)
            {
                continue;
            }

            if (!indexerElem.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var prowlarrIndexerId))
            {
                continue;
            }

            activeProwlarrIds.Add(prowlarrIndexerId);
            result.TotalFound++;

            var rawName = indexerElem.TryGetProperty("name", out var nProp) && !string.IsNullOrWhiteSpace(nProp.GetString())
                ? nProp.GetString()
                : $"Indexer {prowlarrIndexerId}";

            var indexerName = rawName.StartsWith("Prowlarr - ", StringComparison.OrdinalIgnoreCase)
                ? rawName
                : $"Prowlarr - {rawName}";

            var categoryList = ExtractCategories(indexerElem);
            var categoriesStr = categoryList.Count > 0 ? string.Join(",", categoryList) : null;
            var torznabUrl = $"{hostUrl}/{prowlarrIndexerId}";
            var apiKey = prowlarrDef.ApiKey ?? string.Empty;

            var existingLocal = localSyncedIndexers.FirstOrDefault(l =>
                ProwlarrSyncMetadata.GetProwlarrIndexerId(l) == prowlarrIndexerId);

            if (existingLocal == null)
            {
                existingLocal = localSyncedIndexers.FirstOrDefault(l =>
                    l.Url != null && (l.Url.TrimEnd('/') == torznabUrl || l.Url.TrimEnd('/') == $"{torznabUrl}/api"));
            }

            var settingsJson = ProwlarrSyncMetadata.BuildSettingsJson(prowlarrIndexerId, prowlarrDef.Id > 0 ? prowlarrDef.Id : null);

            if (existingLocal == null)
            {
                var newDef = new IndexerDefinition
                {
                    Name = indexerName,
                    IndexerType = "Torznab",
                    Implementation = "TorznabIndexer",
                    ConfigContract = ProwlarrSyncMetadata.ConfigContractName,
                    Url = torznabUrl,
                    ApiKey = apiKey,
                    ApiPath = "/api",
                    Enable = true,
                    EnableRss = true,
                    EnableSearch = true,
                    Categories = categoriesStr,
                    DownloadClientId = prowlarrDef.DownloadClientId,
                    Tags = prowlarrDef.Tags != null ? new List<int>(prowlarrDef.Tags) : new List<int>(),
                    Settings = settingsJson
                };

                _indexerRepository.Insert(newDef);
                result.Added++;
                result.SyncedIndexers.Add(indexerName);
                _logger.Info("Added Prowlarr Torznab indexer: {0} ({1})", indexerName, torznabUrl);
            }
            else
            {
                var mutated = false;

                if (existingLocal.Name != indexerName)
                {
                    existingLocal.Name = indexerName;
                    mutated = true;
                }

                if (existingLocal.Url != torznabUrl)
                {
                    existingLocal.Url = torznabUrl;
                    mutated = true;
                }

                if (existingLocal.ApiKey != apiKey)
                {
                    existingLocal.ApiKey = apiKey;
                    mutated = true;
                }

                if (existingLocal.Categories != categoriesStr)
                {
                    existingLocal.Categories = categoriesStr;
                    mutated = true;
                }

                if (existingLocal.ApiPath != "/api")
                {
                    existingLocal.ApiPath = "/api";
                    mutated = true;
                }

                if (existingLocal.ConfigContract != ProwlarrSyncMetadata.ConfigContractName)
                {
                    existingLocal.ConfigContract = ProwlarrSyncMetadata.ConfigContractName;
                    mutated = true;
                }

                if (existingLocal.Settings != settingsJson)
                {
                    existingLocal.Settings = settingsJson;
                    mutated = true;
                }

                if (mutated)
                {
                    _indexerRepository.Update(existingLocal);
                    result.Updated++;
                    result.SyncedIndexers.Add(indexerName);
                    _logger.Info("Updated Prowlarr Torznab indexer: {0} ({1})", indexerName, torznabUrl);
                }
            }
        }

        // Prune removed indexers
        foreach (var local in localSyncedIndexers)
        {
            var localProwlarrId = ProwlarrSyncMetadata.GetProwlarrIndexerId(local);
            if (!localProwlarrId.HasValue || !activeProwlarrIds.Contains(localProwlarrId.Value))
            {
                _indexerRepository.Delete(local.Id);
                result.Removed++;
                _logger.Info(
                    "Pruned removed Prowlarr indexer: {0} (Id={1}, ProwlarrId={2})",
                    local.Name,
                    local.Id,
                    localProwlarrId);
            }
        }
    }

    private static List<int> ExtractCategories(JsonElement indexerElem)
    {
        var categories = new HashSet<int>();

        if (indexerElem.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object)
        {
            if (caps.TryGetProperty("categories", out var catsElem) && catsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in catsElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var catNum))
                    {
                        categories.Add(catNum);
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        if (item.TryGetProperty("id", out var catIdProp) && catIdProp.TryGetInt32(out var catId))
                        {
                            categories.Add(catId);
                        }

                        if (item.TryGetProperty("subCategories", out var subCats) && subCats.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var sub in subCats.EnumerateArray())
                            {
                                if (sub.ValueKind == JsonValueKind.Number && sub.TryGetInt32(out var subNum))
                                {
                                    categories.Add(subNum);
                                }
                                else if (sub.ValueKind == JsonValueKind.Object &&
                                         sub.TryGetProperty("id", out var subIdProp) &&
                                         subIdProp.TryGetInt32(out var subId))
                                {
                                    categories.Add(subId);
                                }
                            }
                        }
                    }
                }
            }
        }

        if (indexerElem.TryGetProperty("categories", out var directCats) && directCats.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in directCats.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var num))
                {
                    categories.Add(num);
                }
                else if (item.ValueKind == JsonValueKind.Object &&
                         item.TryGetProperty("id", out var idProp) &&
                         idProp.TryGetInt32(out var id))
                {
                    categories.Add(id);
                }
            }
        }

        return categories.OrderBy(c => c).ToList();
    }
}
