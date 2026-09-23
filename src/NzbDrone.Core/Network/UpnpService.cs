using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using Open.Nat;

namespace NzbDrone.Core.Network;

public class PortMapping
{
    public int InternalPort { get; set; }
    public int ExternalPort { get; set; }
    public string Protocol { get; set; }
    public string Description { get; set; }
    public bool IsActive { get; set; }
    public string ErrorMessage { get; set; }
    public int LeaseSeconds { get; set; }
    public DateTime? ExpiryUtc { get; set; }
}

public class UpnpMappingCreatedEvent : IEvent
{
    public int ExternalPort { get; }

    public UpnpMappingCreatedEvent(int externalPort)
    {
        ExternalPort = externalPort;
    }
}

public interface IUpnpDevice
{
    Task<System.Net.IPAddress> GetExternalIPAsync();
    Task CreatePortMapAsync(Mapping mapping);
    Task DeletePortMapAsync(Mapping mapping);
}

public class UpnpDeviceWrapper : IUpnpDevice
{
    private readonly NatDevice _device;

    public UpnpDeviceWrapper(NatDevice device)
    {
        _device = device;
    }

    public Task<System.Net.IPAddress> GetExternalIPAsync() => _device.GetExternalIPAsync();
    public Task CreatePortMapAsync(Mapping mapping) => _device.CreatePortMapAsync(mapping);
    public Task DeletePortMapAsync(Mapping mapping) => _device.DeletePortMapAsync(mapping);
}

public interface IUpnpService
{
    List<PortMapping> GetMappings();
    bool IsAvailable { get; }
    string ExternalIp { get; }
    DateTime? LastRenewalUtc { get; }
    DateTime? NextRenewalUtc { get; }
    string RouterModel { get; }
    Task CreateMappings(CancellationToken stoppingToken);
}

public class UpnpService : BackgroundService, IUpnpService
{
    private const int LifetimeSeconds = 7200;

    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly List<PortMapping> _mappings = new();
    private readonly Func<CancellationToken, Task<IUpnpDevice>> _deviceDiscoverer;
    private IUpnpDevice _discoveredDevice;

    public bool IsAvailable { get; private set; }
    public string ExternalIp { get; private set; } = "";
    public DateTime? LastRenewalUtc { get; private set; }
    public DateTime? NextRenewalUtc { get; private set; }
    public string RouterModel { get; set; } = "";

    public const PortMapper SupportedPortMappers = PortMapper.Upnp | PortMapper.Pmp;

    public UpnpService(
        IConfigService configService,
        IEventAggregator eventAggregator,
        Func<CancellationToken, Task<IUpnpDevice>> deviceDiscoverer = null,
        Func<PortMapper, CancellationTokenSource, Task<IUpnpDevice>> portMapperDiscoverer = null)
    {
        _configService = configService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        if (portMapperDiscoverer != null)
        {
            _deviceDiscoverer = async ct =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                return await portMapperDiscoverer(SupportedPortMappers, cts);
            };
        }
        else
        {
            _deviceDiscoverer = deviceDiscoverer ?? (async ct =>
            {
                var discoverer = new NatDiscoverer();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var device = await discoverer.DiscoverDeviceAsync(SupportedPortMappers, cts);
                return new UpnpDeviceWrapper(device);
            });
        }
    }

    public List<PortMapping> GetMappings()
    {
        lock (_mappings)
        {
            return new List<PortMapping>(_mappings);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configService.UpnpEnabled)
        {
            _logger.Info("UPnP disabled in configuration");
            return;
        }

        await CreateMappings(stoppingToken);

        // Renew mappings periodically
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(LifetimeSeconds / 2), stoppingToken);
                await CreateMappings(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await RemoveMappings();
    }

    public async Task CreateMappings(CancellationToken stoppingToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var device = await _deviceDiscoverer(cts.Token);
            _discoveredDevice = device;

            IsAvailable = true;
            LastRenewalUtc = DateTime.UtcNow;
            NextRenewalUtc = DateTime.UtcNow.AddSeconds(LifetimeSeconds / 2);
            if (string.IsNullOrEmpty(RouterModel))
            {
                RouterModel = "UPnP-IGD Gateway";
            }

            try
            {
                var externalIp = await device.GetExternalIPAsync().WaitAsync(TimeSpan.FromSeconds(10), stoppingToken);
                ExternalIp = externalIp?.ToString() ?? "";
                _logger.Info("UPnP device found, external IP: {0}", ExternalIp);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "UPnP: Failed to retrieve external IP address from gateway");
            }

            var peerPort = _configService.ListeningPort;
            var trackerPort = _configService.TrackerHttpPort;
            var trackerUdpPort = _configService.TrackerUdpPort;

            var peerTcpSuccess = await MapPort(device, peerPort, Protocol.Tcp, "Seedarr Peer", stoppingToken);
            await MapPort(device, peerPort, Protocol.Udp, "Seedarr DHT", stoppingToken);

            if (_configService.TrackerServerEnabled && _configService.TrackerHttpEnabled && trackerPort > 0)
            {
                await MapPort(device, trackerPort, Protocol.Tcp, "Seedarr Tracker HTTP", stoppingToken);
            }
            else
            {
                await UnmapPortByDescription(device, "Seedarr Tracker HTTP", stoppingToken);
            }

            if (_configService.TrackerServerEnabled && _configService.TrackerUdpEnabled && trackerUdpPort > 0)
            {
                await MapPort(device, trackerUdpPort, Protocol.Udp, "Seedarr Tracker UDP", stoppingToken);
            }
            else
            {
                await UnmapPortByDescription(device, "Seedarr Tracker UDP", stoppingToken);
            }

            if (peerTcpSuccess)
            {
                _eventAggregator.PublishEvent(new UpnpMappingCreatedEvent(peerPort));
            }
            else
            {
                _logger.Warn("UPnP: Peer port {0} TCP mapping was not created; skipping UpnpMappingCreatedEvent", peerPort);
            }
        }
        catch (NatDeviceNotFoundException)
        {
            _logger.Warn("No UPnP device found");
            _discoveredDevice = null;
            IsAvailable = false;
            MarkAllMappingsInactive("No UPnP device found");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "UPnP mapping failed");
            _discoveredDevice = null;
            IsAvailable = false;
            MarkAllMappingsInactive(ex.Message);
        }
    }

    public async Task<bool> MapPort(IUpnpDevice device, int port, Protocol protocol, string description, CancellationToken cancellationToken = default)
    {
        var protocolName = protocol.ToString().ToUpperInvariant();
        try
        {
            var mapping = new Mapping(protocol, port, port, LifetimeSeconds, description);
            await device.CreatePortMapAsync(mapping).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            UpdateMappingStatus(new PortMapping
            {
                InternalPort = port,
                ExternalPort = port,
                Protocol = protocolName,
                Description = description,
                IsActive = true,
                ErrorMessage = null,
                LeaseSeconds = LifetimeSeconds,
                ExpiryUtc = DateTime.UtcNow.AddSeconds(LifetimeSeconds)
            });
            _logger.Info("UPnP: mapped {0} port {1} ({2})", protocolName, port, description);
            return true;
        }
        catch (Exception ex)
        {
            var (isConflict, errorMsg) = AnalyzeMappingException(ex, port, protocolName);
            if (isConflict)
            {
                _logger.Warn("UPnP: port conflict detected on {0} port {1} (Code 718): {2}", protocolName, port, errorMsg);
            }
            else
            {
                _logger.Warn(ex, "UPnP: failed to map {0} port {1}: {2}", protocolName, port, ex.Message);
            }

            UpdateMappingStatus(new PortMapping
            {
                InternalPort = port,
                ExternalPort = port,
                Protocol = protocolName,
                Description = description,
                IsActive = false,
                ErrorMessage = errorMsg,
                LeaseSeconds = 0,
                ExpiryUtc = null
            });
            return false;
        }
    }

    private async Task UnmapPortByDescription(IUpnpDevice device, string description, CancellationToken cancellationToken)
    {
        PortMapping existing;
        lock (_mappings)
        {
            existing = _mappings.Find(m => string.Equals(m.Description, description, StringComparison.OrdinalIgnoreCase) && m.IsActive);
        }

        if (existing == null)
        {
            return;
        }

        try
        {
            var protocol = string.Equals(existing.Protocol, "UDP", StringComparison.OrdinalIgnoreCase)
                ? Protocol.Udp
                : Protocol.Tcp;
            var natMapping = new Mapping(protocol, existing.InternalPort, existing.ExternalPort, 0, existing.Description);
            await device.DeletePortMapAsync(natMapping).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            existing.IsActive = false;
            existing.LeaseSeconds = 0;
            existing.ExpiryUtc = null;
            existing.ErrorMessage = "Mapping removed";
            _logger.Info("UPnP: unmapped disabled {0} port {1} ({2})", existing.Protocol, existing.InternalPort, description);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UPnP: failed to unmap disabled mapping {0}:{1}", existing.Protocol, existing.InternalPort);
        }
    }

    public async Task RemoveMappings()
    {
        if (_discoveredDevice == null)
        {
            _logger.Debug("UPnP: No discovered device available for teardown; skipping discovery");
            IsAvailable = false;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            var device = _discoveredDevice;

            List<PortMapping> snapshot;
            lock (_mappings)
            {
                snapshot = new List<PortMapping>(_mappings);
            }

            foreach (var portMapping in snapshot)
            {
                if (!portMapping.IsActive)
                {
                    continue;
                }
                cts.Token.ThrowIfCancellationRequested();

                try
                {
                    var protocol = string.Equals(portMapping.Protocol, "UDP", StringComparison.OrdinalIgnoreCase)
                        ? Protocol.Udp
                        : Protocol.Tcp;
                    var natMapping = new Mapping(protocol, portMapping.InternalPort, portMapping.ExternalPort, 0, portMapping.Description);
                    await device.DeletePortMapAsync(natMapping).WaitAsync(cts.Token);
                    portMapping.IsActive = false;
                    portMapping.LeaseSeconds = 0;
                    portMapping.ExpiryUtc = null;
                    portMapping.ErrorMessage = "Mapping removed";
                    _logger.Info("UPnP: removed {0} port {1}", portMapping.Protocol, portMapping.InternalPort);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "UPnP: failed to remove mapping {0}:{1}", portMapping.Protocol, portMapping.InternalPort);
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.Debug(ex, "UPnP teardown timed out or was cancelled");
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UPnP cleanup failed");
        }
        finally
        {
            _discoveredDevice = null;
            IsAvailable = false;
        }
    }

    private static (bool IsConflict, string ErrorMessage) AnalyzeMappingException(Exception ex, int port, string protocolName)
    {
        var mappingEx = ex as MappingException ?? ex.InnerException as MappingException;
        if (mappingEx != null && mappingEx.ErrorCode == 718)
        {
            return (true, $"Conflict in mapping entry (Error 718): External port {port}/{protocolName} is already in use by another device or application on the gateway.");
        }

        if (ex.Message.Contains("718") || ex.Message.Contains("ConflictInMappingEntry", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"Conflict in mapping entry (Error 718): External port {port}/{protocolName} is already in use by another device or application on the gateway.");
        }

        return (false, ex.Message);
    }

    private void UpdateMappingStatus(PortMapping mapping)
    {
        lock (_mappings)
        {
            var existing = _mappings.Find(m => m.InternalPort == mapping.InternalPort && m.Protocol == mapping.Protocol);
            if (existing != null)
            {
                existing.IsActive = mapping.IsActive;
                existing.ErrorMessage = mapping.ErrorMessage;
                existing.ExternalPort = mapping.ExternalPort;
                existing.Description = mapping.Description;
                existing.LeaseSeconds = mapping.LeaseSeconds;
                existing.ExpiryUtc = mapping.ExpiryUtc;
            }
            else
            {
                _mappings.Add(mapping);
            }
        }
    }

    private void MarkAllMappingsInactive(string errorMessage)
    {
        lock (_mappings)
        {
            foreach (var m in _mappings)
            {
                m.IsActive = false;
                m.LeaseSeconds = 0;
                m.ExpiryUtc = null;
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    m.ErrorMessage = errorMessage;
                }
            }
        }
    }
}
