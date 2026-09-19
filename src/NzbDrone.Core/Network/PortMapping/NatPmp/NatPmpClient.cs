using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Network.NatPmp;

public interface INatPmpTransport : IDisposable
{
    Task<byte[]> SendAndReceiveAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default);
    Task SendAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default);
}

public class DefaultNatPmpTransport : INatPmpTransport
{
    private readonly UdpClient _udpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public DefaultNatPmpTransport(AddressFamily addressFamily = AddressFamily.InterNetwork)
    {
        _udpClient = new UdpClient(addressFamily);
        _ownsClient = true;
    }

    public DefaultNatPmpTransport(UdpClient udpClient)
    {
        _udpClient = udpClient ?? throw new ArgumentNullException(nameof(udpClient));
        _ownsClient = false;
    }

    public async Task<byte[]> SendAndReceiveAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        int[] timeouts = [250, 500, 1000, 2000];

        foreach (var timeoutMs in timeouts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _udpClient.SendAsync(request, endpoint, cancellationToken);

                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCts.CancelAfter(timeoutMs);

                var result = await _udpClient.ReceiveAsync(receiveCts.Token);
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

        throw new TimeoutException($"NAT-PMP request to {endpoint} timed out after retries.");
    }

    public async Task SendAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        await _udpClient.SendAsync(request, endpoint, cancellationToken);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsClient)
            {
                _udpClient?.Dispose();
            }

            _disposed = true;
        }
    }
}

public class NatPmpEpochResetEventArgs : EventArgs
{
    public uint PreviousEpoch { get; }
    public uint NewEpoch { get; }

    public NatPmpEpochResetEventArgs(uint previousEpoch, uint newEpoch)
    {
        PreviousEpoch = previousEpoch;
        NewEpoch = newEpoch;
    }
}

public record NatPmpMappingResult(
    NatPmpProtocol Protocol,
    ushort InternalPort,
    ushort MappedExternalPort,
    uint LifetimeSeconds,
    uint Epoch);

public class NatPmpActiveMapping
{
    public NatPmpProtocol Protocol { get; init; }
    public ushort InternalPort { get; init; }
    public ushort SuggestedExternalPort { get; set; }
    public ushort MappedExternalPort { get; set; }
    public uint LifetimeSeconds { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime RenewalDueUtc { get; set; }
    internal ITimer RenewalTimer { get; set; }
}

public interface INatPmpClient : IDisposable, IAsyncDisposable
{
    IPAddress GatewayAddress { get; }
    uint? LastEpoch { get; }
    IReadOnlyList<NatPmpActiveMapping> ActiveMappings { get; }
    event EventHandler<NatPmpEpochResetEventArgs> EpochReset;

    Task<IPAddress> GetExternalIpAsync(CancellationToken cancellationToken = default);
    Task<NatPmpMappingResult> MapPortAsync(
        NatPmpProtocol protocol,
        ushort internalPort,
        ushort suggestedExternalPort = 0,
        uint? lifetimeSeconds = null,
        CancellationToken cancellationToken = default);
    Task UnmapPortAsync(NatPmpProtocol protocol, ushort internalPort, CancellationToken cancellationToken = default);
    Task RenewAllMappingsAsync(CancellationToken cancellationToken = default);
    Task ReleaseAllMappingsAsync(CancellationToken cancellationToken = default);
}

public class NatPmpClient : INatPmpClient
{
    public const uint DefaultLifetimeSeconds = 3600;

    private readonly IPAddress _gatewayAddress;
    private readonly IPEndPoint _gatewayEndPoint;
    private readonly INatPmpTransport _transport;
    private readonly bool _ownsTransport;
    private readonly TimeProvider _timeProvider;
    private readonly uint _defaultLifetimeSeconds;
    private readonly Logger _logger;
    private readonly Dictionary<(NatPmpProtocol Protocol, ushort InternalPort), NatPmpActiveMapping> _mappings = new();
    private readonly object _syncLock = new();
    private readonly IDisposable _stoppingRegistration;
    private uint? _lastEpoch;
    private bool _disposed;

    public IPAddress GatewayAddress => _gatewayAddress;
    public uint? LastEpoch => _lastEpoch;

    public IReadOnlyList<NatPmpActiveMapping> ActiveMappings
    {
        get
        {
            lock (_syncLock)
            {
                return _mappings.Values.ToList();
            }
        }
    }

    public event EventHandler<NatPmpEpochResetEventArgs> EpochReset;

    public NatPmpClient(
        IPAddress gatewayAddress,
        INatPmpTransport transport = null,
        TimeProvider timeProvider = null,
        uint defaultLifetimeSeconds = DefaultLifetimeSeconds,
        IHostApplicationLifetime appLifetime = null)
    {
        _gatewayAddress = gatewayAddress ?? throw new ArgumentNullException(nameof(gatewayAddress));
        _gatewayEndPoint = new IPEndPoint(_gatewayAddress, NatPmpPacket.NatPmpPort);
        _logger = LogManager.GetCurrentClassLogger();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _defaultLifetimeSeconds = defaultLifetimeSeconds;

        if (transport != null)
        {
            _transport = transport;
            _ownsTransport = false;
        }
        else
        {
            _transport = new DefaultNatPmpTransport(gatewayAddress.AddressFamily);
            _ownsTransport = true;
        }

        if (appLifetime != null)
        {
            _stoppingRegistration = appLifetime.ApplicationStopping.Register(() =>
            {
                _logger.Info("Application shutting down: releasing active NAT-PMP port mappings");
                try
                {
                    ReleaseAllMappingsNonBlocking();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error releasing NAT-PMP mappings on application shutdown");
                }
            });
        }
    }

    public async Task<IPAddress> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var request = NatPmpPacket.CreateExternalIpRequest();
        var responseBuffer = await _transport.SendAndReceiveAsync(request, _gatewayEndPoint, cancellationToken);
        var response = NatPmpPacket.ParseExternalIpResponse(responseBuffer);

        if (response.ResultCode != NatPmpResultCode.Success)
        {
            throw new NatPmpException(response.ResultCode, $"NAT-PMP external IP request failed: {NatPmpPacket.GetResultCodeMessage(response.ResultCode)}");
        }

        ProcessEpoch(response.Epoch);
        return response.ExternalAddress;
    }

    public async Task<NatPmpMappingResult> MapPortAsync(
        NatPmpProtocol protocol,
        ushort internalPort,
        ushort suggestedExternalPort = 0,
        uint? lifetimeSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var lifetime = lifetimeSeconds ?? _defaultLifetimeSeconds;
        var request = NatPmpPacket.CreatePortMappingRequest(protocol, internalPort, suggestedExternalPort, lifetime);
        var responseBuffer = await _transport.SendAndReceiveAsync(request, _gatewayEndPoint, cancellationToken);
        var response = NatPmpPacket.ParsePortMappingResponse(responseBuffer);

        if (response.ResultCode != NatPmpResultCode.Success)
        {
            throw new NatPmpException(response.ResultCode, $"NAT-PMP port mapping failed for {protocol} port {internalPort}: {NatPmpPacket.GetResultCodeMessage(response.ResultCode)}");
        }

        ProcessEpoch(response.Epoch);

        if (response.Lifetime == 0)
        {
            RemoveActiveMapping(protocol, internalPort);
            return new NatPmpMappingResult(protocol, internalPort, response.MappedExternalPort, 0, response.Epoch);
        }

        var mapping = UpdateOrAddActiveMapping(protocol, internalPort, suggestedExternalPort, response.MappedExternalPort, response.Lifetime);
        ScheduleRenewal(mapping);

        return new NatPmpMappingResult(protocol, internalPort, response.MappedExternalPort, response.Lifetime, response.Epoch);
    }

    public async Task UnmapPortAsync(NatPmpProtocol protocol, ushort internalPort, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        RemoveActiveMapping(protocol, internalPort);

        var request = NatPmpPacket.CreateDeletePortMappingRequest(protocol, internalPort);
        await _transport.SendAsync(request, _gatewayEndPoint, cancellationToken);
    }

    public async Task RenewAllMappingsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        List<NatPmpActiveMapping> mappingsSnapshot;
        lock (_syncLock)
        {
            mappingsSnapshot = _mappings.Values.ToList();
        }

        foreach (var mapping in mappingsSnapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RenewMappingInternalAsync(mapping, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to renew NAT-PMP mapping for {0} port {1}", mapping.Protocol, mapping.InternalPort);
            }
        }
    }

    public async Task ReleaseAllMappingsAsync(CancellationToken cancellationToken = default)
    {
        List<NatPmpActiveMapping> snapshot;
        lock (_syncLock)
        {
            snapshot = _mappings.Values.ToList();
            foreach (var mapping in snapshot)
            {
                mapping.RenewalTimer?.Dispose();
                mapping.RenewalTimer = null;
            }

            _mappings.Clear();
        }

        foreach (var mapping in snapshot)
        {
            try
            {
                var deletePacket = NatPmpPacket.CreateDeletePortMappingRequest(mapping.Protocol, mapping.InternalPort);
                await _transport.SendAsync(deletePacket, _gatewayEndPoint, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send zero-lease deletion for {0} port {1}", mapping.Protocol, mapping.InternalPort);
            }
        }
    }

    private void ReleaseAllMappingsNonBlocking()
    {
        List<NatPmpActiveMapping> snapshot;
        lock (_syncLock)
        {
            snapshot = _mappings.Values.ToList();
            foreach (var mapping in snapshot)
            {
                mapping.RenewalTimer?.Dispose();
                mapping.RenewalTimer = null;
            }

            _mappings.Clear();
        }

        foreach (var mapping in snapshot)
        {
            try
            {
                var deletePacket = NatPmpPacket.CreateDeletePortMappingRequest(mapping.Protocol, mapping.InternalPort);
                _ = _transport.SendAsync(deletePacket, _gatewayEndPoint, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send non-blocking zero-lease deletion for {0} port {1}", mapping.Protocol, mapping.InternalPort);
            }
        }
    }

    private async Task RenewMappingInternalAsync(NatPmpActiveMapping mapping, CancellationToken cancellationToken)
    {
        var request = NatPmpPacket.CreatePortMappingRequest(
            mapping.Protocol,
            mapping.InternalPort,
            suggestedExternalPort: mapping.MappedExternalPort,
            lifetimeSeconds: mapping.LifetimeSeconds);

        var responseBuffer = await _transport.SendAndReceiveAsync(request, _gatewayEndPoint, cancellationToken);
        var response = NatPmpPacket.ParsePortMappingResponse(responseBuffer);

        if (response.ResultCode != NatPmpResultCode.Success)
        {
            _logger.Warn("Failed to renew NAT-PMP mapping for {0} port {1}: {2}", mapping.Protocol, mapping.InternalPort, NatPmpPacket.GetResultCodeMessage(response.ResultCode));
            return;
        }

        ProcessEpoch(response.Epoch);

        lock (_syncLock)
        {
            if (_mappings.TryGetValue((mapping.Protocol, mapping.InternalPort), out var current) && ReferenceEquals(current, mapping))
            {
                mapping.MappedExternalPort = response.MappedExternalPort;
                mapping.LifetimeSeconds = response.Lifetime;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                mapping.RenewalDueUtc = now.AddSeconds(response.Lifetime / 2.0);
                ScheduleRenewal(mapping);
            }
        }
    }

    private void ProcessEpoch(uint currentEpoch)
    {
        var rebootDetected = false;
        uint previousEpoch = 0;

        lock (_syncLock)
        {
            if (_lastEpoch.HasValue && currentEpoch < _lastEpoch.Value)
            {
                rebootDetected = true;
                previousEpoch = _lastEpoch.Value;
                _logger.Warn("NAT-PMP gateway reboot detected: epoch decreased from {0} to {1}. Re-applying active mappings.", previousEpoch, currentEpoch);
            }

            _lastEpoch = currentEpoch;
        }

        if (rebootDetected)
        {
            try
            {
                EpochReset?.Invoke(this, new NatPmpEpochResetEventArgs(previousEpoch, currentEpoch));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Exception in NatPmpClient.EpochReset event handler");
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await RenewAllMappingsAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to re-apply mappings after NAT-PMP gateway reboot");
                }
            });
        }
    }

    private NatPmpActiveMapping UpdateOrAddActiveMapping(
        NatPmpProtocol protocol,
        ushort internalPort,
        ushort suggestedExternalPort,
        ushort mappedExternalPort,
        uint lifetimeSeconds)
    {
        lock (_syncLock)
        {
            var key = (protocol, internalPort);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var renewalDue = now.AddSeconds(lifetimeSeconds / 2.0);

            if (_mappings.TryGetValue(key, out var existing))
            {
                existing.SuggestedExternalPort = suggestedExternalPort;
                existing.MappedExternalPort = mappedExternalPort;
                existing.LifetimeSeconds = lifetimeSeconds;
                existing.RenewalDueUtc = renewalDue;
                return existing;
            }

            var newMapping = new NatPmpActiveMapping
            {
                Protocol = protocol,
                InternalPort = internalPort,
                SuggestedExternalPort = suggestedExternalPort,
                MappedExternalPort = mappedExternalPort,
                LifetimeSeconds = lifetimeSeconds,
                CreatedAtUtc = now,
                RenewalDueUtc = renewalDue
            };

            _mappings[key] = newMapping;
            return newMapping;
        }
    }

    private void ScheduleRenewal(NatPmpActiveMapping mapping)
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            mapping.RenewalTimer?.Dispose();

            var halfLifeSeconds = mapping.LifetimeSeconds / 2.0;
            if (halfLifeSeconds <= 0)
            {
                return;
            }

            var delay = TimeSpan.FromSeconds(halfLifeSeconds);

            mapping.RenewalTimer = _timeProvider.CreateTimer(
                state =>
                {
                    var m = (NatPmpActiveMapping)state;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await RenewMappingInternalAsync(m, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Error in half-life renewal timer for {0} port {1}", m.Protocol, m.InternalPort);
                        }
                    });
                },
                mapping,
                delay,
                Timeout.InfiniteTimeSpan);
        }
    }

    private void RemoveActiveMapping(NatPmpProtocol protocol, ushort internalPort)
    {
        lock (_syncLock)
        {
            if (_mappings.Remove((protocol, internalPort), out var mapping))
            {
                mapping.RenewalTimer?.Dispose();
                mapping.RenewalTimer = null;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NatPmpClient));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stoppingRegistration?.Dispose();

        try
        {
            ReleaseAllMappingsNonBlocking();
        }
        catch
        {
            // Ignore teardown errors during dispose
        }

        if (_ownsTransport)
        {
            _transport.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stoppingRegistration?.Dispose();

        try
        {
            await ReleaseAllMappingsAsync(CancellationToken.None);
        }
        catch
        {
            // Ignore teardown errors during dispose
        }

        if (_ownsTransport)
        {
            _transport.Dispose();
        }
    }
}
