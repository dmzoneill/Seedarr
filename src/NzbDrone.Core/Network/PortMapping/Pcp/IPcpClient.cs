using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network.Pcp;

public record PcpMappingResult
{
    public bool Success { get; init; }
    public byte Version { get; init; } = 2;
    public PcpResultCode ResultCode { get; init; }
    public TimeSpan Lifetime { get; init; }
    public uint Epoch { get; init; }
    public PortMappingTransport Protocol { get; init; }
    public int InternalPort { get; init; }
    public int ExternalPort { get; init; }
    public IPAddress ExternalAddress { get; init; }
    public byte[] Nonce { get; init; }
    public string ErrorMessage { get; init; }
}

public class PcpActiveMapping
{
    public PortMappingTransport Protocol { get; init; }
    public int InternalPort { get; init; }
    public int SuggestedExternalPort { get; set; }
    public int AssignedExternalPort { get; set; }
    public IPAddress AssignedExternalAddress { get; set; }
    public TimeSpan Lifetime { get; set; }
    public byte[] Nonce { get; init; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime RenewalDueUtc { get; set; }
    public bool IsActive { get; set; } = true;
    internal ITimer RenewalTimer { get; set; }
}

public interface IPcpTransport : IDisposable
{
    Task<byte[]> SendAndReceiveAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default);
    Task SendAsync(byte[] request, IPEndPoint endpoint, CancellationToken cancellationToken = default);
}

public interface IPcpClient : IDisposable
{
    IPAddress ExternalAddress { get; }
    uint? LastEpoch { get; }
    IReadOnlyList<PcpActiveMapping> ActiveMappings { get; }

    Task<PcpMappingResult> CreateMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        int suggestedExternalPort,
        PortMappingTransport protocol,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<PcpMappingResult> CreateMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        int suggestedExternalPort,
        PortMappingTransport protocol,
        TimeSpan lifetime,
        byte[] nonce,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        PortMappingTransport protocol,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteMappingAsync(
        IPAddress gateway,
        IPAddress clientIp,
        int internalPort,
        PortMappingTransport protocol,
        byte[] nonce,
        CancellationToken cancellationToken = default);

    Task ReleaseAllMappingsAsync(IPAddress gateway, IPAddress clientIp, CancellationToken cancellationToken = default);
}
