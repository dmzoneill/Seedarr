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
}

public class UpnpMappingCreatedEvent : IEvent
{
    public int ExternalPort { get; }

    public UpnpMappingCreatedEvent(int externalPort)
    {
        ExternalPort = externalPort;
    }
}

public interface IUpnpService
{
    List<PortMapping> GetMappings();
    bool IsAvailable { get; }
    string ExternalIp { get; }
}

public class UpnpService : BackgroundService, IUpnpService
{
    private const int LifetimeSeconds = 7200;

    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly List<PortMapping> _mappings = new();

    public bool IsAvailable { get; private set; }
    public string ExternalIp { get; private set; } = "";

    public UpnpService(IConfigService configService, IEventAggregator eventAggregator)
    {
        _configService = configService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
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

    private async Task CreateMappings(CancellationToken stoppingToken)
    {
        try
        {
            var discoverer = new NatDiscoverer();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);

            IsAvailable = true;
            ExternalIp = (await device.GetExternalIPAsync()).ToString();
            _logger.Info("UPnP device found, external IP: {0}", ExternalIp);

            var peerPort = _configService.ListeningPort;
            var trackerPort = _configService.TrackerHttpPort;

            await MapPort(device, peerPort, Protocol.Tcp, "Seedarr Peer");
            await MapPort(device, trackerPort, Protocol.Tcp, "Seedarr Tracker HTTP");
            await MapPort(device, peerPort, Protocol.Udp, "Seedarr DHT");

            _eventAggregator.PublishEvent(new UpnpMappingCreatedEvent(peerPort));
        }
        catch (NatDeviceNotFoundException)
        {
            _logger.Warn("No UPnP device found");
            IsAvailable = false;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "UPnP mapping failed");
            IsAvailable = false;
        }
    }

    private async Task MapPort(NatDevice device, int port, Protocol protocol, string description)
    {
        try
        {
            var mapping = new Mapping(protocol, port, port, LifetimeSeconds, description);
            await device.CreatePortMapAsync(mapping);

            var protocolName = protocol.ToString().ToUpperInvariant();

            lock (_mappings)
            {
                var existing = _mappings.Find(m => m.InternalPort == port && m.Protocol == protocolName);
                if (existing != null)
                {
                    existing.IsActive = true;
                }
                else
                {
                    _mappings.Add(new PortMapping
                    {
                        InternalPort = port,
                        ExternalPort = port,
                        Protocol = protocolName,
                        Description = description,
                        IsActive = true
                    });
                }
            }

            _logger.Info("UPnP: mapped {0} port {1} ({2})", protocolName, port, description);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "UPnP: failed to map {0} port {1}", protocol, port);
        }
    }

    private async Task RemoveMappings()
    {
        try
        {
            var discoverer = new NatDiscoverer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);

            List<PortMapping> snapshot;
            lock (_mappings)
            {
                snapshot = new List<PortMapping>(_mappings);
            }

            foreach (var portMapping in snapshot)
            {
                try
                {
                    var protocol = string.Equals(portMapping.Protocol, "UDP", StringComparison.OrdinalIgnoreCase)
                        ? Protocol.Udp
                        : Protocol.Tcp;
                    var natMapping = new Mapping(protocol, portMapping.InternalPort, portMapping.ExternalPort, 0, portMapping.Description);
                    await device.DeletePortMapAsync(natMapping);
                    portMapping.IsActive = false;
                    _logger.Info("UPnP: removed {0} port {1}", portMapping.Protocol, portMapping.InternalPort);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "UPnP: failed to remove mapping {0}:{1}", portMapping.Protocol, portMapping.InternalPort);
                }
            }
        }
        catch (NatDeviceNotFoundException)
        {
            _logger.Debug("UPnP: no device found during cleanup");
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UPnP cleanup failed");
        }
    }
}
