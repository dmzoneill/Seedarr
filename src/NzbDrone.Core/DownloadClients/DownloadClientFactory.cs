using System;
using System.Collections.Concurrent;
using NzbDrone.Common;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.DownloadClients;

public interface IDownloadClientFactory : IProviderFactory<IDownloadClient, DownloadClientDefinition>
{
    IDownloadClient CreateClient(DownloadClientDefinition definition);
    void Invalidate(int id);
    void ClearCache();
}

public class DownloadClientFactory : ProviderFactory<IDownloadClient, DownloadClientDefinition>, IDownloadClientFactory, IDisposable
{
    private readonly IRemotePathMappingService _remotePathMappingService;
    private readonly ConcurrentDictionary<int, CachedClientEntry> _clientCache = new();
    private readonly ConcurrentDictionary<string, IDownloadClient> _unpersistedCache = new();
    private readonly object _syncRoot = new();
    private bool _disposed;

    private class CachedClientEntry
    {
        public string ConfigSignature { get; set; }
        public IDownloadClient Client { get; set; }
    }

    public DownloadClientFactory(
        IDownloadClientRepository providerRepository,
        IServiceFactory serviceFactory,
        IRemotePathMappingService remotePathMappingService = null)
        : base(providerRepository, serviceFactory)
    {
        _remotePathMappingService = remotePathMappingService;
    }

    public IDownloadClient CreateClient(DownloadClientDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        var configKey = GetConfigKey(definition);

        if (definition.Id > 0)
        {
            if (_clientCache.TryGetValue(definition.Id, out var existingEntry))
            {
                if (existingEntry.ConfigSignature == configKey)
                {
                    return existingEntry.Client;
                }

                Invalidate(definition.Id);
            }

            lock (_syncRoot)
            {
                if (_clientCache.TryGetValue(definition.Id, out existingEntry))
                {
                    if (existingEntry.ConfigSignature == configKey)
                    {
                        return existingEntry.Client;
                    }

                    Invalidate(definition.Id);
                }

                var newClient = BuildClient(definition);
                if (newClient != null)
                {
                    _clientCache[definition.Id] = new CachedClientEntry
                    {
                        ConfigSignature = configKey,
                        Client = newClient,
                    };
                }

                return newClient;
            }
        }
        else
        {
            if (_unpersistedCache.TryGetValue(configKey, out var existingClient))
            {
                return existingClient;
            }

            lock (_syncRoot)
            {
                if (_unpersistedCache.TryGetValue(configKey, out existingClient))
                {
                    return existingClient;
                }

                var newClient = BuildClient(definition);
                if (newClient != null)
                {
                    _unpersistedCache[configKey] = newClient;
                }

                return newClient;
            }
        }
    }

    public override void Update(DownloadClientDefinition definition)
    {
        if (definition != null)
        {
            Invalidate(definition.Id);
        }

        base.Update(definition);
    }

    public override void Delete(int id)
    {
        Invalidate(id);
        base.Delete(id);
    }

    public void Invalidate(int id)
    {
        if (_clientCache.TryRemove(id, out var entry))
        {
            (entry.Client as IDisposable)?.Dispose();
        }
    }

    public void ClearCache()
    {
        foreach (var kvp in _clientCache)
        {
            if (_clientCache.TryRemove(kvp.Key, out var entry))
            {
                (entry.Client as IDisposable)?.Dispose();
            }
        }

        foreach (var kvp in _unpersistedCache)
        {
            if (_unpersistedCache.TryRemove(kvp.Key, out var client))
            {
                (client as IDisposable)?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearCache();
    }

    private static string GetConfigKey(DownloadClientDefinition definition)
    {
        return $"{definition.ClientType}|{definition.Host}|{definition.Port}|{definition.UseSsl}|{definition.UrlBase}|{definition.Username}|{definition.Password}|{definition.Category}";
    }

    private IDownloadClient BuildClient(DownloadClientDefinition definition)
    {
        return definition.ClientType switch
        {
            "QBitTorrent" => new NzbDrone.Core.DownloadClients.QBitTorrent.QBitTorrentClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
            },
            "Transmission" => new NzbDrone.Core.DownloadClients.Transmission.TransmissionClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
                RemotePathMappingService = _remotePathMappingService,
            },
            "Deluge" => new NzbDrone.Core.DownloadClients.Deluge.DelugeClient
            {
                Host = definition.Host,
                Port = definition.Port,
                UseSsl = definition.UseSsl,
                Username = definition.Username,
                Password = definition.Password,
                Category = definition.Category,
                RemotePathMappingService = _remotePathMappingService,
            },
            _ => null,
        };
    }
}
