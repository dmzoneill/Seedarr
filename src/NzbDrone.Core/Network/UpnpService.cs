using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

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
    private const int PeerPort = 6881;
    private const int TrackerPort = 9696;
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
        if (!_configService.GetValueBoolean("EnableUpnp", false))
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
            var device = await discoverer.DiscoverDeviceAsync(stoppingToken);

            if (device == null)
            {
                _logger.Warn("No UPnP device found");
                IsAvailable = false;
                return;
            }

            IsAvailable = true;
            ExternalIp = (await device.GetExternalIPAsync()).ToString();
            _logger.Info("UPnP device found, external IP: {0}", ExternalIp);

            await MapPort(device, PeerPort, "TCP", "Seedarr Peer");
            await MapPort(device, TrackerPort, "TCP", "Seedarr Tracker HTTP");
            await MapPort(device, PeerPort, "UDP", "Seedarr DHT");

            _eventAggregator.PublishEvent(new UpnpMappingCreatedEvent(PeerPort));
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "UPnP mapping failed");
            IsAvailable = false;
        }
    }

    private async Task MapPort(INatDevice device, int port, string protocol, string description)
    {
        try
        {
            var mapping = new Mapping(protocol, port, port, LifetimeSeconds, description);
            await device.CreatePortMapAsync(mapping);

            lock (_mappings)
            {
                var existing = _mappings.Find(m => m.InternalPort == port && m.Protocol == protocol);
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
                        Protocol = protocol,
                        Description = description,
                        IsActive = true
                    });
                }
            }

            _logger.Info("UPnP: mapped {0} port {1} ({2})", protocol, port, description);
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
            var device = await discoverer.DiscoverDeviceAsync(cts.Token);

            if (device == null)
            {
                return;
            }

            lock (_mappings)
            {
                foreach (var mapping in _mappings)
                {
                    try
                    {
                        var natMapping = new Mapping(mapping.Protocol, mapping.InternalPort, mapping.ExternalPort, 0, mapping.Description);
                        device.DeletePortMapAsync(natMapping).GetAwaiter().GetResult();
                        mapping.IsActive = false;
                        _logger.Info("UPnP: removed {0} port {1}", mapping.Protocol, mapping.InternalPort);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "UPnP: failed to remove mapping {0}:{1}", mapping.Protocol, mapping.InternalPort);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "UPnP cleanup failed");
        }
    }
}

// Stubs for Open.NAT types — replaced when the NuGet is added
internal class NatDiscoverer
{
    public Task<INatDevice> DiscoverDeviceAsync(CancellationToken token)
    {
        return Task.FromResult<INatDevice>(null);
    }
}

internal interface INatDevice
{
    Task CreatePortMapAsync(Mapping mapping);
    Task DeletePortMapAsync(Mapping mapping);
    Task<System.Net.IPAddress> GetExternalIPAsync();
}

internal class Mapping
{
    public string Protocol { get; }
    public int InternalPort { get; }
    public int ExternalPort { get; }
    public int Lifetime { get; }
    public string Description { get; }

    public Mapping(string protocol, int internalPort, int externalPort, int lifetime, string description)
    {
        Protocol = protocol;
        InternalPort = internalPort;
        ExternalPort = externalPort;
        Lifetime = lifetime;
        Description = description;
    }
}
