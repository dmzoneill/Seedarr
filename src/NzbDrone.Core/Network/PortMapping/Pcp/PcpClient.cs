using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Network.Pcp;

public class DefaultPcpTransport : IPcpTransport
{
    private readonly ConcurrentDictionary<AddressFamily, UdpClient> _clients = new();
    private readonly UdpClient _fixedClient;
    private readonly bool _ownsClients;
    private bool _disposed;

    public DefaultPcpTransport()
    {
        _ownsClients = true;
    }

    public DefaultPcpTransport(AddressFamily addressFamily)
    {
        _clients[addressFamily] = new UdpClient(addressFamily);
        _ownsClients = true;
    }

    public DefaultPcpTransport(UdpClient udpClient)
    {
        _fixedClient = udpClient ?? throw new ArgumentNullException(nameof(udpClient));
        _ownsClients = false;
    }

    public async Task<byte[]> SendAndReceiveAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var client = GetClient(endpoint.AddressFamily);
        int[] timeouts = [250, 500, 1000, 2000];

        foreach (var timeoutMs in timeouts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await client.SendAsync(request, endpoint, cancellationToken);

                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCts.CancelAfter(timeoutMs);

                var result = await client.ReceiveAsync(receiveCts.Token);
                if (result.Buffer != null && result.Buffer.Length > 0)
                {
                    return result.Buffer;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout on this attempt, retry with backoff interval
            }
        }

        throw new TimeoutException($"PCP request to {endpoint} timed out after retries.");
    }

    public async Task SendAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var client = GetClient(endpoint.AddressFamily);
        await client.SendAsync(request, endpoint, cancellationToken);
    }

    private UdpClient GetClient(AddressFamily family)
    {
        if (_fixedClient != null)
        {
            return _fixedClient;
        }

        return _clients.GetOrAdd(family, af => new UdpClient(af));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DefaultPcpTransport));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsClients)
            {
                foreach (var client in _clients.Values)
                {
                    client?.Dispose();
                }

                _clients.Clear();
            }

            _disposed = true;
        }
    }
}

public class PcpClient : IPcpClient
{
    private readonly IPcpTransport _transport;
    private readonly bool _ownsTransport;
    private readonly TimeProvider _timeProvider;
    private readonly Logger _logger;
    private readonly Dictionary<(PortMappingTransport Protocol, int InternalPort), PcpActiveMapping> _mappings = new();
    private readonly object _syncLock = new();
    private readonly IDisposable _stoppingRegistration;
    private IPAddress _externalAddress;
    private uint? _lastEpoch;
    private bool _disposed;

    public IPAddress ExternalAddress => _externalAddress;
    public uint? LastEpoch => _lastEpoch;

    public IReadOnlyList<PcpActiveMapping> ActiveMappings
    {
        get
        {
            lock (_syncLock)
            {
                return _mappings.Values.ToList();
            }
        }
    }

    public PcpClient(
        IPcpTransport transport = null,
        TimeProvider timeProvider = null,
        IHostApplicationLifetime appLifetime = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (transport != null)
        {
            _transport = transport;
            _ownsTransport = false;
        }
        else
        {
            _transport = new DefaultPcpTransport();
            _ownsTransport = true;
        }

        if (appLifetime != null)
        {
            _stoppingRegistration = appLifetime.ApplicationStopping.Register(() =>
            {
                _logger.Info("Application shutting down: releasing active PCP port mappings");
                try
                {
                    ReleaseAllMappingsNonBlocking();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error releasing PCP mappings on application shutdown");
                }
            });
        }
    }

    public async Task<PcpMappingResult> CreateMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        int suggestedExternalPort,
        PortMappingTransport protocol,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        return await CreateMappingAsync(gateway, clientIp, internalPort, suggestedExternalPort, protocol, lifetime, nonce: null, cancellationToken);
    }

    public async Task<PcpMappingResult> CreateMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        int suggestedExternalPort,
        PortMappingTransport protocol,
        TimeSpan lifetime,
        byte[] nonce,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (gateway == null)
        {
            throw new ArgumentNullException(nameof(gateway));
        }

        if (clientIp == null)
        {
            throw new ArgumentNullException(nameof(clientIp));
        }

        var actualNonce = nonce ?? PcpPacket.GenerateNonce();
        var lifetimeSeconds = (uint)Math.Max(0, lifetime.TotalSeconds);
        var request = PcpPacket.CreateMapRequest(clientIp, internalPort, suggestedExternalPort, protocol, lifetimeSeconds, actualNonce);

        var endpoint = new IPEndPoint(gateway, PcpPacket.PcpPort);
        byte[] responseBuffer;
        try
        {
            responseBuffer = await _transport.SendAndReceiveAsync(request, endpoint, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to send/receive PCP MAP request to {0}", endpoint);
            throw;
        }

        var response = PcpPacket.ParseMapResponse(responseBuffer, actualNonce);
        _lastEpoch = response.Epoch;

        if (response.ResultCode != PcpResultCode.Success)
        {
            _logger.Warn("PCP MAP request failed with result code {0}: {1}", response.ResultCode, PcpPacket.GetResultCodeMessage(response.ResultCode));
            return new PcpMappingResult
            {
                Success = false,
                Version = response.Version,
                ResultCode = response.ResultCode,
                Lifetime = TimeSpan.Zero,
                Epoch = response.Epoch,
                Protocol = protocol,
                InternalPort = internalPort,
                ExternalPort = 0,
                ExternalAddress = null,
                Nonce = actualNonce,
                ErrorMessage = PcpPacket.GetResultCodeMessage(response.ResultCode)
            };
        }

        _externalAddress = response.AssignedExternalAddress;

        var grantedLifetime = TimeSpan.FromSeconds(response.LifetimeSeconds);
        if (response.LifetimeSeconds == 0)
        {
            RemoveActiveMapping(protocol, internalPort);
            return new PcpMappingResult
            {
                Success = true,
                Version = response.Version,
                ResultCode = PcpResultCode.Success,
                Lifetime = TimeSpan.Zero,
                Epoch = response.Epoch,
                Protocol = protocol,
                InternalPort = internalPort,
                ExternalPort = response.AssignedExternalPort,
                ExternalAddress = response.AssignedExternalAddress,
                Nonce = actualNonce
            };
        }

        var activeMapping = UpdateOrAddActiveMapping(protocol, internalPort, suggestedExternalPort, response.AssignedExternalPort, response.AssignedExternalAddress, grantedLifetime, actualNonce);
        ScheduleRenewal(gateway, clientIp, activeMapping);

        return new PcpMappingResult
        {
            Success = true,
            Version = response.Version,
            ResultCode = PcpResultCode.Success,
            Lifetime = grantedLifetime,
            Epoch = response.Epoch,
            Protocol = protocol,
            InternalPort = internalPort,
            ExternalPort = response.AssignedExternalPort,
            ExternalAddress = response.AssignedExternalAddress,
            Nonce = actualNonce
        };
    }

    public async Task<bool> DeleteMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        PortMappingTransport protocol,
        CancellationToken cancellationToken = default)
    {
        byte[] nonce = null;
        lock (_syncLock)
        {
            if (_mappings.TryGetValue((protocol, internalPort), out var existing))
            {
                nonce = existing.Nonce;
            }
        }

        return await DeleteMappingAsync(gateway, clientIp, internalPort, protocol, nonce, cancellationToken);
    }

    public async Task<bool> DeleteMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        PortMappingTransport protocol,
        byte[] nonce,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (gateway == null)
        {
            throw new ArgumentNullException(nameof(gateway));
        }

        if (clientIp == null)
        {
            throw new ArgumentNullException(nameof(clientIp));
        }

        var actualNonce = nonce;
        if (actualNonce == null)
        {
            lock (_syncLock)
            {
                if (_mappings.TryGetValue((protocol, internalPort), out var existing))
                {
                    actualNonce = existing.Nonce;
                }
            }
        }

        actualNonce ??= PcpPacket.GenerateNonce();

        RemoveActiveMapping(protocol, internalPort);

        var request = PcpPacket.CreateDeleteMapRequest(clientIp, internalPort, protocol, actualNonce);
        var endpoint = new IPEndPoint(gateway, PcpPacket.PcpPort);

        try
        {
            var responseBuffer = await _transport.SendAndReceiveAsync(request, endpoint, cancellationToken);
            var response = PcpPacket.ParseMapResponse(responseBuffer, actualNonce);
            return response.ResultCode == PcpResultCode.Success;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Exception deleting PCP mapping for {0} port {1}", protocol, internalPort);
            return false;
        }
    }

    public async Task ReleaseAllMappingsAsync(IPAddress gateway, IPAddress clientIp, CancellationToken cancellationToken = default)
    {
        List<PcpActiveMapping> snapshot;
        lock (_syncLock)
        {
            snapshot = _mappings.Values.ToList();
            foreach (var m in snapshot)
            {
                m.RenewalTimer?.Dispose();
                m.RenewalTimer = null;
            }

            _mappings.Clear();
        }

        foreach (var m in snapshot)
        {
            try
            {
                var request = PcpPacket.CreateDeleteMapRequest(clientIp, m.InternalPort, m.Protocol, m.Nonce);
                var endpoint = new IPEndPoint(gateway, PcpPacket.PcpPort);
                await _transport.SendAsync(request, endpoint, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send zero-lifetime delete request for {0} port {1}", m.Protocol, m.InternalPort);
            }
        }
    }

    private void ReleaseAllMappingsNonBlocking()
    {
        lock (_syncLock)
        {
            var snapshot = _mappings.Values.ToList();
            foreach (var m in snapshot)
            {
                m.RenewalTimer?.Dispose();
                m.RenewalTimer = null;
            }

            _mappings.Clear();
        }
    }

    private void ScheduleRenewal(IPAddress gateway, IPAddress clientIp, PcpActiveMapping mapping)
    {
        mapping.RenewalTimer?.Dispose();

        var halfLifetimeSeconds = mapping.Lifetime.TotalSeconds / 2.0;
        if (halfLifetimeSeconds <= 0)
        {
            return;
        }

        var halfLifetime = TimeSpan.FromSeconds(halfLifetimeSeconds);

        mapping.RenewalTimer = _timeProvider.CreateTimer(
            async _ =>
            {
                try
                {
                    await CreateMappingAsync(gateway, clientIp, mapping.InternalPort, mapping.AssignedExternalPort, mapping.Protocol, mapping.Lifetime, mapping.Nonce, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Background renewal failed for PCP {0} port {1}", mapping.Protocol, mapping.InternalPort);
                }
            },
            null,
            halfLifetime,
            Timeout.InfiniteTimeSpan);
    }

    private PcpActiveMapping UpdateOrAddActiveMapping(
        PortMappingTransport protocol,
        int internalPort,
        int suggestedExternalPort,
        int assignedExternalPort,
        IPAddress assignedExternalAddress,
        TimeSpan lifetime,
        byte[] nonce)
    {
        lock (_syncLock)
        {
            var key = (protocol, internalPort);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var renewalDue = now.AddSeconds(lifetime.TotalSeconds / 2.0);

            if (_mappings.TryGetValue(key, out var existing))
            {
                existing.SuggestedExternalPort = suggestedExternalPort;
                existing.AssignedExternalPort = assignedExternalPort;
                existing.AssignedExternalAddress = assignedExternalAddress;
                existing.Lifetime = lifetime;
                existing.RenewalDueUtc = renewalDue;
                existing.IsActive = true;
                return existing;
            }

            var newMapping = new PcpActiveMapping
            {
                Protocol = protocol,
                InternalPort = internalPort,
                SuggestedExternalPort = suggestedExternalPort,
                AssignedExternalPort = assignedExternalPort,
                AssignedExternalAddress = assignedExternalAddress,
                Lifetime = lifetime,
                Nonce = nonce,
                CreatedAtUtc = now,
                RenewalDueUtc = renewalDue,
                IsActive = true
            };

            _mappings[key] = newMapping;
            return newMapping;
        }
    }

    private void RemoveActiveMapping(PortMappingTransport protocol, int internalPort)
    {
        lock (_syncLock)
        {
            if (_mappings.Remove((protocol, internalPort), out var mapping))
            {
                mapping.RenewalTimer?.Dispose();
                mapping.RenewalTimer = null;
                mapping.IsActive = false;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PcpClient));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _stoppingRegistration?.Dispose();

            lock (_syncLock)
            {
                foreach (var m in _mappings.Values)
                {
                    m.RenewalTimer?.Dispose();
                    m.RenewalTimer = null;
                }

                _mappings.Clear();
            }

            if (_ownsTransport)
            {
                _transport?.Dispose();
            }

            _disposed = true;
        }
    }
}
