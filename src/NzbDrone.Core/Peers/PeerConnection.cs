using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Peers.Messages;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Torrents.Merkle;

namespace NzbDrone.Core.Peers;

public class PeerConnection : IDisposable
{
    private const string ProtocolString = "BitTorrent protocol";
    private const int MaxMessageLength = 16 * 1024 * 1024;

    private readonly TcpClient _client;
    private readonly Stream _networkStream;
    private readonly Logger _logger;
    private readonly object _writeLock = new();
    private readonly object _disposeLock = new();
    private bool _isDisposed;
    private Stream _activeStream;

    public string RemoteIp { get; }
    public int RemotePort { get; }
    public string InfoHash { get; set; }
    public string InfoHashV2 { get; set; }
    public Torrent MatchedTorrent { get; set; }
    public IClientProfile ClientProfile { get; set; }
    public int? ExpectedInfoHashLength { get; set; }
    private static IBandwidthLimiter _defaultBandwidthLimiter = NzbDrone.Core.Bandwidth.BandwidthLimiter.Instance;

    public static IBandwidthLimiter DefaultBandwidthLimiter
    {
        get => _defaultBandwidthLimiter;
        set => _defaultBandwidthLimiter = value;
    }

    public IBandwidthLimiter BandwidthLimiter { get; set; } = _defaultBandwidthLimiter;

    private string _peerId;
    public string PeerId
    {
        get => _peerId;
        set
        {
            _peerId = value;
            if (BandwidthLimiter != null && !string.IsNullOrEmpty(_peerId) && (_uploadRateLimit > 0 || _downloadRateLimit > 0))
            {
                BandwidthLimiter.SetPeerLimits(_peerId, _uploadRateLimit, _downloadRateLimit);
            }
        }
    }

    public bool IsInbound { get; set; }
    public string DiscoverySource { get; set; }
    public bool IsLocalPeer => IPAddressExtensions.IsLocalSubnet(RemoteIp) || string.Equals(DiscoverySource, "lpd", StringComparison.OrdinalIgnoreCase);
    public DateTime LastBytesReceived { get; set; } = DateTime.UtcNow;
    public bool IsConnected => !_isDisposed && (_client != null ? _client.Connected : (_activeStream != null));
    public bool IsEncrypted { get; private set; }
    public CryptoMethod EncryptionMethod { get; internal set; }
    public bool AmChoking { get; set; } = true;
    public bool AmInterested { get; set; }
    public bool PeerChoking { get; set; } = true;
    public bool PeerInterested { get; set; }
    public bool IsInterested
    {
        get => AmInterested;
        set => AmInterested = value;
    }

    public bool IsPeerInterested
    {
        get => PeerInterested;
        set => PeerInterested = value;
    }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; }
    public int HandshakeTimeoutMs { get; set; }
    public int MessageReadTimeoutMs { get; set; }
    public int KeepAliveIntervalSeconds { get; set; } = 120;
    public int MaxPipelinedRequests { get; set; } = 200;

    private int _pendingRequestCount;

    public int PendingRequestCount
    {
        get => Volatile.Read(ref _pendingRequestCount);
        set => Interlocked.Exchange(ref _pendingRequestCount, Math.Max(0, value));
    }

    public int DecrementPendingRequests()
    {
        while (true)
        {
            var current = _pendingRequestCount;
            if (current <= 0)
            {
                return 0;
            }

            if (Interlocked.CompareExchange(ref _pendingRequestCount, current - 1, current) == current)
            {
                return current - 1;
            }
        }
    }

    public double IdleChance { get; set; }
    public byte[] ReservedBytes { get; private set; } = new byte[8];
    public bool SupportsExtensionProtocol { get; private set; }
    public bool SupportsFastExtension { get; set; }
    public bool SupportsDht { get; private set; }
    public bool SupportsV2 { get; set; }
    public bool SupportsBep52 => SupportsV2;
    public bool SupportsV2Protocol
    {
        get => SupportsV2;
        set => SupportsV2 = value;
    }

    public event EventHandler<HashRequestMessage> HashRequestReceived;
    public event EventHandler<HashesMessage> HashesReceived;
    public event EventHandler<HashRejectMessage> HashRejectReceived;
    public Action<HashRequestMessage> OnHashRequestReceived { get; set; }
    public Action<HashesMessage> OnHashesReceived { get; set; }
    public Action<HashRejectMessage> OnHashRejectReceived { get; set; }

    public event Action<int> LtDontHaveReceived;
    public Action<int> OnLtDontHaveReceived { get; set; }
    public Func<PeerConnection, bool> InterestEvaluator { get; set; }

    public ConcurrentDictionary<string, MerkleTree> MerkleTrees { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<byte[], MerkleTree> MerkleTreeProvider { get; set; }

    public void RegisterMerkleTree(byte[] piecesRoot, MerkleTree tree)
    {
        if (piecesRoot != null && tree != null)
        {
            MerkleTrees[Convert.ToHexString(piecesRoot)] = tree;
        }
    }

    public MerkleTree GetMerkleTree(byte[] piecesRoot)
    {
        if (piecesRoot == null)
        {
            return null;
        }

        if (MerkleTreeProvider != null)
        {
            var tree = MerkleTreeProvider(piecesRoot);
            if (tree != null)
            {
                return tree;
            }
        }

        return MerkleTrees.TryGetValue(Convert.ToHexString(piecesRoot), out var t) ? t : null;
    }

    public ConcurrentDictionary<string, int> RemoteExtensions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int? RemoteUtPexId
    {
        get => RemoteExtensions.TryGetValue("ut_pex", out var id) ? id : null;
        set
        {
            if (value.HasValue)
            {
                RemoteExtensions["ut_pex"] = value.Value;
            }
            else
            {
                RemoteExtensions.TryRemove("ut_pex", out _);
            }
        }
    }

    public int? RemoteLtDontHaveId
    {
        get => RemoteExtensions.TryGetValue("lt_donthave", out var id) ? id : null;
        set
        {
            if (value.HasValue)
            {
                RemoteExtensions["lt_donthave"] = value.Value;
            }
            else
            {
                RemoteExtensions.TryRemove("lt_donthave", out _);
            }
        }
    }

    public int? LocalLtDontHaveId { get; set; }
    public bool SupportsLtDontHave => RemoteExtensions.TryGetValue("lt_donthave", out var id) && id > 0;

    public int? MetadataSize { get; set; }
    public bool IsSnubbed { get; set; }
    public bool IsOptimisticUnchoked { get; set; }
    public int? AssignedSuperSeedingPiece { get; set; }
    public int AssignedPieceBytesUploaded { get; set; }
    public const int RateWindowSeconds = 20;

    private readonly object _rateLock = new();
    private readonly LinkedList<(DateTime Timestamp, long Uploaded, long Downloaded)> _rateSamples = new();

    public long BytesUploaded { get; set; }
    public long BytesDownloaded { get; set; }
    public long UploadRate { get; set; }
    public long DownloadRate { get; set; }
    public long DownloadSpeed
    {
        get => DownloadRate;
        set => DownloadRate = value;
    }

    public long UploadSpeed
    {
        get => UploadRate;
        set => UploadRate = value;
    }

    public void UpdateTransferRates()
    {
        UpdateTransferRates(DateTime.UtcNow);
    }

    public void UpdateTransferRates(DateTime currentTime)
    {
        lock (_rateLock)
        {
            _rateSamples.AddLast((currentTime, BytesUploaded, BytesDownloaded));

            var cutoff = currentTime.AddSeconds(-RateWindowSeconds);
            while (_rateSamples.Count > 1 && _rateSamples.First.Value.Timestamp < cutoff)
            {
                _rateSamples.RemoveFirst();
            }

            if (_rateSamples.Count < 2)
            {
                return;
            }

            var earliest = _rateSamples.First.Value;
            var latest = _rateSamples.Last.Value;
            var elapsedSeconds = (latest.Timestamp - earliest.Timestamp).TotalSeconds;

            if (elapsedSeconds > 0.001)
            {
                var deltaUp = Math.Max(0, latest.Uploaded - earliest.Uploaded);
                var deltaDown = Math.Max(0, latest.Downloaded - earliest.Downloaded);

                UploadRate = (long)Math.Round(deltaUp / elapsedSeconds);
                DownloadRate = (long)Math.Round(deltaDown / elapsedSeconds);
            }
        }
    }

    public void RecordBytesUploaded(long bytes)
    {
        if (bytes > 0)
        {
            BytesUploaded += bytes;
            if (MatchedTorrent != null)
            {
                MatchedTorrent.RealUploaded += bytes;
            }
        }
    }

    public void RecordBytesDownloaded(long bytes)
    {
        if (bytes > 0)
        {
            BytesDownloaded += bytes;
            LastBytesReceived = DateTime.UtcNow;
        }
    }

    public void ResetTransferRates()
    {
        lock (_rateLock)
        {
            _rateSamples.Clear();
            UploadRate = 0;
            DownloadRate = 0;
        }
    }

    private byte[] _bitfield;
    private bool[] _peerPieces;
    private bool _isPartialSeed;
    private bool _hasMissingWantedPieces = true;

    public virtual bool IsPartialSeed
    {
        get => _isPartialSeed || !_hasMissingWantedPieces;
        set
        {
            _isPartialSeed = value;
            if (value)
            {
                _hasMissingWantedPieces = false;
            }
        }
    }

    public virtual bool HasMissingWantedPieces
    {
        get => _hasMissingWantedPieces && !_isPartialSeed;
        set
        {
            _hasMissingWantedPieces = value;
            if (!value)
            {
                _isPartialSeed = true;
            }
        }
    }

    public virtual byte[] Bitfield
    {
        get
        {
            if (_bitfield != null)
            {
                return _bitfield;
            }

            if (_peerPieces != null && _peerPieces.Length > 0)
            {
                var byteCount = (_peerPieces.Length + 7) / 8;
                var bytes = new byte[byteCount];
                for (var i = 0; i < _peerPieces.Length; i++)
                {
                    if (_peerPieces[i])
                    {
                        bytes[i / 8] |= (byte)(1 << (7 - (i % 8)));
                    }
                }

                return bytes;
            }

            return null;
        }
        set
        {
            _bitfield = value;
            if (value != null)
            {
                if (_peerPieces != null)
                {
                    var count = 0;
                    for (var i = 0; i < _peerPieces.Length; i++)
                    {
                        var byteIndex = i / 8;
                        var bitIndex = 7 - (i % 8);
                        var hasPiece = byteIndex < value.Length && ((value[byteIndex] >> bitIndex) & 1) != 0;
                        _peerPieces[i] = hasPiece;
                        if (hasPiece)
                        {
                            count++;
                        }
                    }

                    HaveCount = count;
                    if (_peerPieces.Length > 0)
                    {
                        Progress = (double)HaveCount / _peerPieces.Length;
                    }
                }
                else
                {
                    var pieceCount = value.Length * 8;
                    _peerPieces = new bool[pieceCount];
                    var count = 0;
                    for (var i = 0; i < pieceCount; i++)
                    {
                        var byteIndex = i / 8;
                        var bitIndex = 7 - (i % 8);
                        var hasPiece = ((value[byteIndex] >> bitIndex) & 1) != 0;
                        _peerPieces[i] = hasPiece;
                        if (hasPiece)
                        {
                            count++;
                        }
                    }

                    HaveCount = count;
                    Progress = pieceCount > 0 ? (double)count / pieceCount : 0.0;
                }
            }
            else
            {
                _peerPieces = null;
                HaveCount = 0;
                Progress = 0.0;
            }
        }
    }

    public double Progress { get; set; }
    public int HaveCount { get; set; }

    public virtual bool[] PeerPieces
    {
        get => _peerPieces;
        set
        {
            _peerPieces = value;
            _bitfield = null;
            if (value != null)
            {
                var count = 0;
                for (var i = 0; i < value.Length; i++)
                {
                    if (value[i])
                    {
                        count++;
                    }
                }

                HaveCount = count;
                Progress = value.Length > 0 ? (double)count / value.Length : 0.0;
            }
            else
            {
                HaveCount = 0;
                Progress = 0.0;
            }
        }
    }

    public HashSet<int> SuggestedPieces { get; } = new();
    public HashSet<int> RemoteAllowedFastPieces { get; } = new();
    public HashSet<int> AllowedFastPieces => RemoteAllowedFastPieces;
    public List<PeerRequest> PendingIncomingRequests { get; } = new();
    public List<PeerRequest> IncomingRequests => PendingIncomingRequests;
    public event Action<PeerConnection, PeerMessage> MessageSent;
    public DateTime LastRequestReceived { get; set; } = DateTime.UtcNow;
    public DateTime? LastUnchokedAt { get; set; }
    public DateTime LastPexReceived { get; set; } = DateTime.MinValue;
    public int PexRateLimitViolations { get; set; }
    public PeerPexTracker PexTracker { get; } = new();
    public bool SupportsPex => RemoteExtensions.TryGetValue("ut_pex", out var id) && id > 0;

    private bool _isSeed;

    public virtual bool IsSeed
    {
        get => _isSeed || Progress >= 1.0 || (PeerPieces != null && PeerPieces.Length > 0 && ((HaveCount > 0 && HaveCount == PeerPieces.Length) || (HaveCount == 0 && PeerPieces.All(p => p))));
        set => _isSeed = value;
    }

    public IDhKeyPool DhKeyPool { get; set; }
    public byte[] InitialApplicationData { get; private set; }
    public bool HandshakeSent { get; private set; }
    public bool EnableIaPipelining { get; set; } = true;

    public const int MinThrottledBufferSize = BandwidthPacer.MinThrottledBufferSize;
    public const int MaxThrottledBufferSize = BandwidthPacer.MaxThrottledBufferSize;
    public const int DefaultUnthrottledBufferSize = BandwidthPacer.DefaultUnthrottledBufferSize;
    public const double DefaultEstimatedRttSeconds = BandwidthPacer.DefaultEstimatedRttSeconds;
    public const int DefaultPacingChunkSize = BandwidthPacer.DefaultPacingChunkSize;

    public static int CalculateBdpBufferSize(
        long rateBytesPerSec,
        double rttSeconds = DefaultEstimatedRttSeconds,
        int minThrottled = MinThrottledBufferSize,
        int maxThrottled = MaxThrottledBufferSize,
        int unthrottled = DefaultUnthrottledBufferSize)
    {
        return BandwidthPacer.CalculateBdpBufferSize(rateBytesPerSec, rttSeconds, minThrottled, maxThrottled, unthrottled);
    }

    private long _uploadRateLimit;
    private long _downloadRateLimit;
    private double _estimatedRttSeconds = DefaultEstimatedRttSeconds;
    private int _pacingChunkSize = DefaultPacingChunkSize;

    public long UploadRateLimit
    {
        get => _uploadRateLimit;
        set
        {
            _uploadRateLimit = value;
            UpdateSocketBuffers();
            if (BandwidthLimiter != null && !string.IsNullOrEmpty(PeerId))
            {
                BandwidthLimiter.SetPeerLimits(PeerId, _uploadRateLimit, _downloadRateLimit);
            }
        }
    }

    public long DownloadRateLimit
    {
        get => _downloadRateLimit;
        set
        {
            _downloadRateLimit = value;
            UpdateSocketBuffers();
            if (BandwidthLimiter != null && !string.IsNullOrEmpty(PeerId))
            {
                BandwidthLimiter.SetPeerLimits(PeerId, _uploadRateLimit, _downloadRateLimit);
            }
        }
    }

    public double EstimatedRttSeconds
    {
        get => _estimatedRttSeconds;
        set
        {
            _estimatedRttSeconds = value > 0 ? value : DefaultEstimatedRttSeconds;
            UpdateSocketBuffers();
        }
    }

    public int PacingChunkSize
    {
        get => _pacingChunkSize;
        set => _pacingChunkSize = Math.Clamp(value, 512, 65536);
    }

    public int SendBufferSize
    {
        get => _client?.Client?.SendBufferSize ?? 0;
        set
        {
            if (_client?.Client != null)
            {
                try
                {
                    _client.Client.SendBufferSize = value;
                }
                catch
                {
                }
            }
        }
    }

    public int ReceiveBufferSize
    {
        get => _client?.Client?.ReceiveBufferSize ?? 0;
        set
        {
            if (_client?.Client != null)
            {
                try
                {
                    _client.Client.ReceiveBufferSize = value;
                }
                catch
                {
                }
            }
        }
    }

    public void SetRateLimits(long uploadRateLimitBytesPerSec, long downloadRateLimitBytesPerSec = 0, double rttSeconds = DefaultEstimatedRttSeconds)
    {
        _uploadRateLimit = uploadRateLimitBytesPerSec;
        _downloadRateLimit = downloadRateLimitBytesPerSec;
        if (rttSeconds > 0)
        {
            _estimatedRttSeconds = rttSeconds;
        }

        UpdateSocketBuffers();

        if (BandwidthLimiter != null && !string.IsNullOrEmpty(PeerId))
        {
            BandwidthLimiter.SetPeerLimits(PeerId, _uploadRateLimit, _downloadRateLimit);
        }
    }

    public void UpdateSocketBuffers()
    {
        if (_client?.Client != null)
        {
            try
            {
                _client.Client.SendBufferSize = CalculateBdpBufferSize(_uploadRateLimit, _estimatedRttSeconds);
            }
            catch
            {
            }

            try
            {
                _client.Client.ReceiveBufferSize = CalculateBdpBufferSize(_downloadRateLimit, _estimatedRttSeconds);
            }
            catch
            {
            }
        }
    }

    internal Action<int, long> OnWriteChunkPaced { get; set; }
    internal static Action<long, long> PaceWaitHandler { get; set; } = BandwidthPacer.PaceWait;

    public PeerConnection(TcpClient client, IDhKeyPool dhKeyPool = null)
    {
        DhKeyPool = dhKeyPool;
        _client = client;
        _networkStream = client?.GetStream();
        _activeStream = _networkStream;
        _logger = LogManager.GetCurrentClassLogger();
        if (client?.Client?.RemoteEndPoint is not IPEndPoint endpoint)
        {
            RemoteIp = "0.0.0.0";
            RemotePort = 0;
        }
        else
        {
            RemoteIp = endpoint.Address.ToString();
            RemotePort = endpoint.Port;
        }

        ApplySocketOptions(_client, 0, 0);

        ConnectedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }

    public PeerConnection(Stream stream, string remoteIp, int remotePort, IDhKeyPool dhKeyPool = null)
    {
        DhKeyPool = dhKeyPool;
        _networkStream = stream ?? throw new ArgumentNullException(nameof(stream));
        _activeStream = _networkStream;
        _logger = LogManager.GetCurrentClassLogger();
        RemoteIp = remoteIp;
        RemotePort = remotePort;
        ConnectedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }

    public PeerConnection(string host, int port, IPAddress localBindAddress = null, int dscp = 0, int tos = 0, Network.IProxySettingsProvider proxySettings = null, IDhKeyPool dhKeyPool = null, string bindInterface = null)
    {
        DhKeyPool = dhKeyPool;
        if (!string.IsNullOrWhiteSpace(bindInterface))
        {
            var family = localBindAddress?.AddressFamily ?? (IPAddress.TryParse(host, out var parsed) ? parsed.AddressFamily : AddressFamily.InterNetwork);
            _client = new TcpClient(family);
            _client.Client.BindToNetworkInterface(bindInterface, localBindAddress, 0);
        }
        else if (localBindAddress != null)
        {
            var localEp = new IPEndPoint(localBindAddress, 0);
            _client = new TcpClient(localEp);
        }
        else
        {
            _client = new TcpClient();
        }

        try
        {
            ApplySocketOptions(_client, dscp, tos);

            if (proxySettings != null && proxySettings.IsEnabled)
            {
                _client.Connect(proxySettings.Host, proxySettings.Port);
                _networkStream = _client.GetStream();

                if (proxySettings.Type == Network.ProxyType.Socks5)
                {
                    PerformSocks5Handshake(_networkStream, host, port, proxySettings.Username, proxySettings.Password);
                }
                else if (proxySettings.Type == Network.ProxyType.Http)
                {
                    PerformHttpConnectHandshake(_networkStream, host, port, proxySettings.Username, proxySettings.Password);
                }
            }
            else
            {
                _client.Connect(host, port);
                _networkStream = _client.GetStream();
            }

            _activeStream = _networkStream;
            _logger = LogManager.GetCurrentClassLogger();
            RemoteIp = host;
            RemotePort = port;
            ConnectedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;
        }
        catch
        {
            _client.Dispose();
            throw;
        }
    }

    public bool NegotiateEncryptionOutgoing(string infoHash, EncryptionMode mode, byte[] initialPayload = null)
    {
        try
        {
            var infoHashBytes = Convert.FromHexString(infoHash);
            var handshake = new MseHandshake(infoHashBytes, mode, DhKeyPool);
            var payload = initialPayload;
            if (payload == null && EnableIaPipelining && !string.IsNullOrEmpty(PeerId))
            {
                payload = BuildHandshake(infoHash, PeerId, isPrivate: MatchedTorrent?.IsPrivate == true, clientProfile: ClientProfile);
            }

            _activeStream = handshake.NegotiateOutgoing(_networkStream ?? _activeStream, payload);
            EncryptionMethod = handshake.NegotiatedMethod;
            InitialApplicationData = handshake.InitialApplicationData;
            IsEncrypted = EncryptionMethod == CryptoMethod.Rc4;
            InfoHash = infoHash;
            if (payload != null && payload.Length > 0)
            {
                HandshakeSent = true;
            }

            LastActivity = DateTime.UtcNow;
            _logger.Debug("MSE/PE outgoing completed with {0}:{1} - method: {2}", RemoteIp, RemotePort, EncryptionMethod);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MSE/PE outgoing negotiation failed with {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public ValueTask<bool> NegotiateEncryptionOutgoingAsync(string infoHash, EncryptionMode mode, CancellationToken cancellationToken = default)
    {
        return NegotiateEncryptionOutgoingAsync(infoHash, mode, null, cancellationToken);
    }

    public async ValueTask<bool> NegotiateEncryptionOutgoingAsync(string infoHash, EncryptionMode mode, byte[] initialPayload, CancellationToken cancellationToken = default)
    {
        try
        {
            var infoHashBytes = Convert.FromHexString(infoHash);
            var handshake = new MseHandshake(infoHashBytes, mode, DhKeyPool);
            var payload = initialPayload;
            if (payload == null && EnableIaPipelining && !string.IsNullOrEmpty(PeerId))
            {
                payload = BuildHandshake(infoHash, PeerId, isPrivate: MatchedTorrent?.IsPrivate == true, clientProfile: ClientProfile);
            }

            _activeStream = await handshake.NegotiateOutgoingAsync(_networkStream ?? _activeStream, payload, cancellationToken);
            EncryptionMethod = handshake.NegotiatedMethod;
            InitialApplicationData = handshake.InitialApplicationData;
            IsEncrypted = EncryptionMethod == CryptoMethod.Rc4;
            InfoHash = infoHash;
            if (payload != null && payload.Length > 0)
            {
                HandshakeSent = true;
            }

            LastActivity = DateTime.UtcNow;
            _logger.Debug("MSE/PE outgoing completed with {0}:{1} - method: {2}", RemoteIp, RemotePort, EncryptionMethod);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MSE/PE outgoing negotiation failed with {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public bool NegotiateEncryptionIncoming(Func<byte[], bool> infoHashValidator, EncryptionMode mode)
    {
        try
        {
            if (HandshakeTimeoutMs > 0)
            {
                if (_client?.Client != null)
                {
                    _client.Client.ReceiveTimeout = HandshakeTimeoutMs;
                }
                else if (_networkStream.CanTimeout)
                {
                    _networkStream.ReadTimeout = HandshakeTimeoutMs;
                }
            }

            // Peek the first byte to detect whether this is an MSE or plain handshake.
            // A standard BT handshake starts with 0x13 (19); MSE starts with the DH public key.
            var peek = new byte[1];
            var read = _networkStream.Read(peek, 0, 1);
            if (read == 0)
            {
                return false;
            }

            if (peek[0] == 19)
            {
                if (mode == EncryptionMode.RequireEncrypted)
                {
                    _logger.Debug("Rejecting unencrypted incoming BitTorrent handshake from {0}:{1} in RequireEncrypted mode", RemoteIp, RemotePort);
                    return false;
                }

                // Plain BitTorrent handshake - feed the peeked byte back through a PrefixedStream
                _activeStream = new PrefixedStream(peek, _networkStream, ownsStream: false);
                EncryptionMethod = CryptoMethod.PlainText;
                InitialApplicationData = null;
                IsEncrypted = false;
                return true;
            }

            // MSE/PE handshake - prefix the peeked byte back
            var prefixed = new PrefixedStream(peek, _networkStream, ownsStream: false);
            var handshake = new MseHandshake(Array.Empty<byte>(), mode, DhKeyPool);
            _activeStream = handshake.NegotiateIncoming(prefixed, infoHashValidator);
            EncryptionMethod = handshake.NegotiatedMethod;
            InitialApplicationData = handshake.InitialApplicationData;
            IsEncrypted = EncryptionMethod == CryptoMethod.Rc4;
            LastActivity = DateTime.UtcNow;
            _logger.Debug("MSE/PE incoming completed with {0}:{1} - method: {2}", RemoteIp, RemotePort, EncryptionMethod);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MSE/PE incoming negotiation failed with {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public async ValueTask<bool> NegotiateEncryptionIncomingAsync(Func<byte[], bool> infoHashValidator, EncryptionMode mode, CancellationToken cancellationToken = default)
    {
        try
        {
            if (HandshakeTimeoutMs > 0)
            {
                if (_client?.Client != null)
                {
                    _client.Client.ReceiveTimeout = HandshakeTimeoutMs;
                }
                else if (_networkStream.CanTimeout)
                {
                    _networkStream.ReadTimeout = HandshakeTimeoutMs;
                }
            }

            // Peek the first byte to detect whether this is an MSE or plain handshake.
            // A standard BT handshake starts with 0x13 (19); MSE starts with the DH public key.
            var peek = new byte[1];
            var read = await _networkStream.ReadAsync(peek.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            if (peek[0] == 19)
            {
                if (mode == EncryptionMode.RequireEncrypted)
                {
                    _logger.Debug("Rejecting unencrypted incoming BitTorrent handshake from {0}:{1} in RequireEncrypted mode", RemoteIp, RemotePort);
                    return false;
                }

                // Plain BitTorrent handshake - feed the peeked byte back through a PrefixedStream
                _activeStream = new PrefixedStream(peek, _networkStream, ownsStream: false);
                EncryptionMethod = CryptoMethod.PlainText;
                InitialApplicationData = null;
                IsEncrypted = false;
                return true;
            }

            // MSE/PE handshake - prefix the peeked byte back
            var prefixed = new PrefixedStream(peek, _networkStream, ownsStream: false);
            var handshake = new MseHandshake(Array.Empty<byte>(), mode, DhKeyPool);
            _activeStream = await handshake.NegotiateIncomingAsync(prefixed, infoHashValidator, cancellationToken);
            EncryptionMethod = handshake.NegotiatedMethod;
            InitialApplicationData = handshake.InitialApplicationData;
            IsEncrypted = EncryptionMethod == CryptoMethod.Rc4;
            LastActivity = DateTime.UtcNow;
            _logger.Debug("MSE/PE incoming completed with {0}:{1} - method: {2}", RemoteIp, RemotePort, EncryptionMethod);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MSE/PE incoming negotiation failed with {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public bool NegotiateEncryptionIncoming(IMseSkeyRegistry skeyRegistry, EncryptionMode mode)
    {
        if (skeyRegistry == null)
        {
            return false;
        }

        return NegotiateEncryptionIncoming(
            skeyHash =>
            {
                if (skeyRegistry.TryMatchTorrent(skeyHash, out var torrent))
                {
                    InfoHash = torrent?.InfoHash;
                    MatchedTorrent = torrent;
                    return true;
                }

                return false;
            },
            mode);
    }

    public async ValueTask<bool> NegotiateEncryptionIncomingAsync(IMseSkeyRegistry skeyRegistry, EncryptionMode mode, CancellationToken cancellationToken = default)
    {
        if (skeyRegistry == null)
        {
            return false;
        }

        return await NegotiateEncryptionIncomingAsync(
            skeyHash =>
            {
                if (skeyRegistry.TryMatchTorrent(skeyHash, out var torrent))
                {
                    InfoHash = torrent?.InfoHash;
                    MatchedTorrent = torrent;
                    return true;
                }

                return false;
            },
            mode,
            cancellationToken);
    }

    public bool NegotiateEncryptionIncoming(Func<byte[], Torrent> torrentValidator, EncryptionMode mode)
    {
        if (torrentValidator == null)
        {
            return false;
        }

        return NegotiateEncryptionIncoming(
            skeyHash =>
            {
                var torrent = torrentValidator(skeyHash);
                if (torrent != null)
                {
                    InfoHash = torrent.InfoHash;
                    MatchedTorrent = torrent;
                    return true;
                }

                return false;
            },
            mode);
    }

    public async ValueTask<bool> NegotiateEncryptionIncomingAsync(Func<byte[], Torrent> torrentValidator, EncryptionMode mode, CancellationToken cancellationToken = default)
    {
        if (torrentValidator == null)
        {
            return false;
        }

        return await NegotiateEncryptionIncomingAsync(
            skeyHash =>
            {
                var torrent = torrentValidator(skeyHash);
                if (torrent != null)
                {
                    InfoHash = torrent.InfoHash;
                    MatchedTorrent = torrent;
                    return true;
                }

                return false;
            },
            mode,
            cancellationToken);
    }

    public bool SendHandshake(
        string infoHash,
        string peerId,
        bool isPrivate = false,
        IClientProfile clientProfile = null,
        bool supportsExtensions = true,
        bool supportsFast = true,
        bool supportsDht = true,
        bool supportsV2 = false)
    {
        try
        {
            ClientProfile = clientProfile;
            if (HandshakeSent && string.Equals(InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
            {
                PeerId = peerId;
                LastActivity = DateTime.UtcNow;
                return true;
            }

            var handshake = BuildHandshake(infoHash, peerId, isPrivate, clientProfile, supportsExtensions, supportsFast, supportsDht, supportsV2);
            _activeStream.Write(handshake, 0, handshake.Length);
            _activeStream.Flush();
            InfoHash = infoHash;
            if (infoHash.Length == 64)
            {
                InfoHashV2 = infoHash.ToLowerInvariant();
            }

            PeerId = peerId;
            HandshakeSent = true;
            LastActivity = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Handshake send failed to {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public bool ReceiveHandshake(int? expectedHashLength = null)
    {
        try
        {
            if (HandshakeTimeoutMs > 0)
            {
                if (_client?.Client != null)
                {
                    _client.Client.ReceiveTimeout = HandshakeTimeoutMs;
                }
                else if (_activeStream.CanTimeout)
                {
                    _activeStream.ReadTimeout = HandshakeTimeoutMs;
                }
            }

            var headerBuffer = new byte[28];
            var read = ReadExact(headerBuffer, 28);
            if (!read)
            {
                return false;
            }

            var pstrlen = headerBuffer[0];
            if (pstrlen != 19)
            {
                return false;
            }

            var pstr = Encoding.ASCII.GetString(headerBuffer, 1, 19);
            if (pstr != ProtocolString)
            {
                return false;
            }

            // reserved bytes at 20-27
            ReservedBytes = new byte[8];
            Array.Copy(headerBuffer, 20, ReservedBytes, 0, 8);
            SupportsExtensionProtocol = (headerBuffer[25] & 0x10) != 0;
            SupportsFastExtension = (headerBuffer[27] & 0x04) != 0;
            SupportsDht = (headerBuffer[27] & 0x01) != 0;
            SupportsV2 = (headerBuffer[27] & 0x10) != 0;

            var hashLength = 20;
            if (expectedHashLength.HasValue && (expectedHashLength.Value == 20 || expectedHashLength.Value == 32))
            {
                hashLength = expectedHashLength.Value;
            }
            else if (ExpectedInfoHashLength.HasValue && (ExpectedInfoHashLength.Value == 20 || ExpectedInfoHashLength.Value == 32))
            {
                hashLength = ExpectedInfoHashLength.Value;
            }
            else if (!string.IsNullOrEmpty(InfoHash))
            {
                hashLength = InfoHash.Length == 64 ? 32 : 20;
            }
            else if (MatchedTorrent != null)
            {
                if (!string.IsNullOrEmpty(MatchedTorrent.InfoHashV2) && (string.IsNullOrEmpty(MatchedTorrent.InfoHash) || MatchedTorrent.InfoHash.Length == 64))
                {
                    hashLength = 32;
                }
                else
                {
                    hashLength = 20;
                }
            }
            else if (_activeStream.CanSeek && (_activeStream.Length - _activeStream.Position) == 52)
            {
                hashLength = 32;
            }
            else if (_activeStream.CanSeek && (_activeStream.Length - _activeStream.Position) == 40)
            {
                hashLength = 20;
            }
            else
            {
                if (_client?.Client != null && _client.Client.Available == 0 && _activeStream.CanRead)
                {
                    try
                    {
                        _client.Client.Poll(200_000, SelectMode.SelectRead);
                    }
                    catch
                    {
                    }
                }

                if (_client?.Client != null)
                {
                    if (_client.Client.Available == 52)
                    {
                        hashLength = 32;
                    }
                    else if (_client.Client.Available == 40)
                    {
                        hashLength = 20;
                    }
                    else if (_client.Client.Available >= 52 && SupportsV2)
                    {
                        hashLength = 32;
                    }
                }
            }

            var hashBuffer = new byte[hashLength];
            if (!ReadExact(hashBuffer, hashLength))
            {
                return false;
            }

            var peerIdBuffer = new byte[20];
            if (!ReadExact(peerIdBuffer, 20))
            {
                return false;
            }

            var hexHash = Convert.ToHexString(hashBuffer).ToLowerInvariant();
            if (hashLength == 32)
            {
                InfoHashV2 = hexHash;
                InfoHash = hexHash;
            }
            else
            {
                InfoHash = hexHash;
            }

            PeerId = Encoding.ASCII.GetString(peerIdBuffer);
            LastActivity = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Handshake receive failed from {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public virtual void SendMessage(PeerMessage message)
    {
        var length = message.Length;
        var bufferSize = 4 + length;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            buffer[0] = (byte)(length >> 24);
            buffer[1] = (byte)(length >> 16);
            buffer[2] = (byte)(length >> 8);
            buffer[3] = (byte)length;
            buffer[4] = (byte)message.Type;

            var payloadLength = message.EffectivePayloadLength;
            if (message.Payload != null && payloadLength > 0)
            {
                Array.Copy(message.Payload, 0, buffer, 5, payloadLength);
            }

            lock (_writeLock)
            {
                if (_isDisposed)
                {
                    return;
                }

                var hasLimiterUpload = BandwidthLimiter != null && BandwidthLimiter.HasUploadLimit(InfoHash, PeerId);
                if ((_uploadRateLimit > 0 || hasLimiterUpload) && bufferSize > _pacingChunkSize)
                {
                    WritePaced(buffer, bufferSize);
                }
                else
                {
                    if (hasLimiterUpload)
                    {
                        BandwidthLimiter.ConsumeUpload(InfoHash, PeerId, bufferSize);
                    }

                    _activeStream.Write(buffer, 0, bufferSize);
                    _activeStream.Flush();
                }
            }

            LastActivity = DateTime.UtcNow;
            MessageSent?.Invoke(this, message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void WritePaced(byte[] buffer, int count)
    {
        var offset = 0;
        var chunkSize = _pacingChunkSize > 0 ? _pacingChunkSize : DefaultPacingChunkSize;

        while (offset < count && !_isDisposed)
        {
            var slice = Math.Min(chunkSize, count - offset);
            var sliceStart = Stopwatch.GetTimestamp();

            if (BandwidthLimiter != null && BandwidthLimiter.HasUploadLimit(InfoHash, PeerId))
            {
                BandwidthLimiter.ConsumeUpload(InfoHash, PeerId, slice);
            }

            _activeStream.Write(buffer, offset, slice);
            _activeStream.Flush();
            offset += slice;

            if (offset < count && _uploadRateLimit > 0)
            {
                var targetTicks = (long)((double)slice * Stopwatch.Frequency / _uploadRateLimit);
                OnWriteChunkPaced?.Invoke(slice, targetTicks);
                PaceWaitHandler(sliceStart, targetTicks);
            }
        }
    }

    public virtual void SendExtendedMessage(byte extensionId, byte[] payload)
    {
        var length = payload?.Length ?? 0;
        var extendedPayload = new byte[1 + length];
        extendedPayload[0] = extensionId;
        if (payload != null && length > 0)
        {
            Array.Copy(payload, 0, extendedPayload, 1, length);
        }

        SendMessage(new PeerMessage
        {
            Type = PeerMessageType.Extended,
            Payload = extendedPayload
        });
    }

    public virtual void SendExtendedMessage(int extensionId, byte[] payload)
    {
        SendExtendedMessage((byte)extensionId, payload);
    }

    public virtual bool SendExtendedMessage(string extensionName, byte[] payload)
    {
        if (RemoteExtensions.TryGetValue(extensionName, out var extId) && extId > 0)
        {
            SendExtendedMessage((byte)extId, payload);
            return true;
        }

        return false;
    }

    public void SendKeepAlive()
    {
        var buffer = new byte[4];
        lock (_writeLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _activeStream.Write(buffer, 0, 4);
            _activeStream.Flush();
        }

        LastActivity = DateTime.UtcNow;
    }

    internal ArrayPool<byte> BufferPool { get; set; } = ArrayPool<byte>.Shared;
    private int _bytesReadForCurrentMessage;

    public virtual async ValueTask<PeerMessage> ReceiveMessageAsync(CancellationToken cancellationToken = default)
    {
        _bytesReadForCurrentMessage = 0;

        if (_isDisposed)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource timeoutCts = null;
        var linkedToken = cancellationToken;

        if (MessageReadTimeoutMs > 0)
        {
            if (_client?.Client != null)
            {
                _client.Client.ReceiveTimeout = MessageReadTimeoutMs;
            }
            else if (_activeStream.CanTimeout)
            {
                _activeStream.ReadTimeout = MessageReadTimeoutMs;
            }

            timeoutCts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
            timeoutCts.CancelAfter(MessageReadTimeoutMs);
            linkedToken = timeoutCts.Token;
        }

        try
        {
            var lengthBuffer = BufferPool.Rent(4);
            int length;
            try
            {
                if (!await ReadExactAsync(lengthBuffer.AsMemory(0, 4), linkedToken))
                {
                    Dispose();
                    return null;
                }

                length = (int)(((uint)lengthBuffer[0] << 24) | ((uint)lengthBuffer[1] << 16) |
                    ((uint)lengthBuffer[2] << 8) | lengthBuffer[3]);
            }
            finally
            {
                BufferPool.Return(lengthBuffer);
            }

            if (length == 0)
            {
                LastActivity = DateTime.UtcNow;
                return null; // keep-alive
            }

            if (length < 0 || length > MaxMessageLength)
            {
                _logger.Warn("Peer {0}:{1} sent message with length {2} exceeding max {3}, closing connection", RemoteIp, RemotePort, length, MaxMessageLength);
                Dispose();
                return null;
            }

            var messageBuffer = BufferPool.Rent(length);
            try
            {
                if (!await ReadExactAsync(messageBuffer.AsMemory(0, length), linkedToken))
                {
                    Dispose();
                    return null;
                }

                var message = new PeerMessage
                {
                    Type = (PeerMessageType)messageBuffer[0]
                };

                if (length > 1)
                {
                    message.Payload = new byte[length - 1];
                    Array.Copy(messageBuffer, 1, message.Payload, 0, length - 1);
                }

                if (message.Type == PeerMessageType.Piece)
                {
                    var payloadSize = message.Payload != null ? message.Payload.Length : 0;
                    var pieceDataSize = payloadSize > 8 ? payloadSize - 8 : 0;
                    BytesDownloaded += pieceDataSize;
                }

                LastActivity = DateTime.UtcNow;
                return message;
            }
            finally
            {
                BufferPool.Return(messageBuffer);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (_bytesReadForCurrentMessage > 0)
            {
                Dispose();
            }

            throw;
        }
        catch (OperationCanceledException ex)
        {
            if (_bytesReadForCurrentMessage > 0)
            {
                _logger.Debug(ex, "Timeout or read error during partial message framing from {0}:{1}; terminating connection", RemoteIp, RemotePort);
                Dispose();
            }

            return null;
        }
        catch (Exception ex) when (IsTimeoutException(ex))
        {
            if (_bytesReadForCurrentMessage > 0)
            {
                _logger.Debug(ex, "Timeout or read error during partial message framing from {0}:{1}; terminating connection", RemoteIp, RemotePort);
                Dispose();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error reading message from peer {0}:{1}", RemoteIp, RemotePort);
            Dispose();
            return null;
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    public PeerMessage ReceiveMessage()
    {
        var bytesReadForCurrentMessage = 0;

        try
        {
            if (_isDisposed)
            {
                return null;
            }

            if (MessageReadTimeoutMs > 0)
            {
                if (_client?.Client != null)
                {
                    _client.Client.ReceiveTimeout = MessageReadTimeoutMs;
                }
                else if (_activeStream.CanTimeout)
                {
                    _activeStream.ReadTimeout = MessageReadTimeoutMs;
                }
            }

            var lengthBuffer = new byte[4];
            if (!ReadExact(lengthBuffer, 4, ref bytesReadForCurrentMessage))
            {
                Dispose();
                return null;
            }

            var length = (int)(((uint)lengthBuffer[0] << 24) | ((uint)lengthBuffer[1] << 16) |
                ((uint)lengthBuffer[2] << 8) | lengthBuffer[3]);

            if (length == 0)
            {
                LastActivity = DateTime.UtcNow;
                return null; // keep-alive
            }

            if (length < 0 || length > MaxMessageLength)
            {
                _logger.Warn("Peer {0}:{1} sent message with length {2} exceeding max {3}, closing connection", RemoteIp, RemotePort, length, MaxMessageLength);
                Dispose();
                return null;
            }

            var messageBuffer = new byte[length];
            if (!ReadExact(messageBuffer, length, ref bytesReadForCurrentMessage))
            {
                Dispose();
                return null;
            }

            var message = new PeerMessage
            {
                Type = (PeerMessageType)messageBuffer[0]
            };

            if (length > 1)
            {
                message.Payload = new byte[length - 1];
                Array.Copy(messageBuffer, 1, message.Payload, 0, length - 1);
            }

            if (message.Type == PeerMessageType.Piece)
            {
                var payloadSize = message.Payload != null ? message.Payload.Length : 0;
                var pieceDataSize = payloadSize > 8 ? payloadSize - 8 : 0;
                BytesDownloaded += pieceDataSize;
            }

            LastActivity = DateTime.UtcNow;
            return message;
        }
        catch (Exception ex) when (IsTimeoutException(ex))
        {
            if (bytesReadForCurrentMessage > 0)
            {
                _logger.Debug(ex, "Timeout or read error during partial message framing from {0}:{1}; terminating connection", RemoteIp, RemotePort);
                Dispose();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error reading message from peer {0}:{1}", RemoteIp, RemotePort);
            Dispose();
            return null;
        }
    }

    public virtual void SendBitfield(byte[] bitfield)
    {
        if (bitfield == null || bitfield.Length == 0)
        {
            return;
        }

        SendMessage(new PeerMessage { Type = PeerMessageType.Bitfield, Payload = bitfield });
    }

    public virtual void SendBitfield(ReadOnlyMemory<byte> bitfield)
    {
        SendBitfield(bitfield.ToArray());
    }

    public virtual void SendHashRequest(HashRequestMessage request)
    {
        if (request != null)
        {
            SendMessage(request.ToPeerMessage());
        }
    }

    public virtual void SendHashRequest(byte[] piecesRoot, int baseLayer, int index, int length, int proofLayers)
    {
        SendHashRequest(new HashRequestMessage(piecesRoot, baseLayer, index, length, proofLayers));
    }

    public virtual void SendHashes(HashesMessage hashes)
    {
        if (hashes != null)
        {
            SendMessage(hashes.ToPeerMessage());
        }
    }

    public virtual void SendHashes(byte[] piecesRoot, int baseLayer, int index, int length, int proofLayers, byte[] hashes)
    {
        SendHashes(new HashesMessage(piecesRoot, baseLayer, index, length, proofLayers, hashes));
    }

    public virtual void SendHashReject(HashRejectMessage reject)
    {
        if (reject != null)
        {
            SendMessage(reject.ToPeerMessage());
        }
    }

    public virtual void SendHashReject(byte[] piecesRoot, int baseLayer, int index, int length, int proofLayers)
    {
        SendHashReject(new HashRejectMessage(piecesRoot, baseLayer, index, length, proofLayers));
    }

    public virtual void SendHashReject(HashRequestMessage request)
    {
        SendHashReject(new HashRejectMessage(request));
    }

    public virtual void HandleMessage(PeerMessage message)
    {
        if (message == null)
        {
            return;
        }

        switch (message.Type)
        {
            case PeerMessageType.HashRequest:
                try
                {
                    var req = HashRequestMessage.FromPeerMessage(message);
                    HashRequestReceived?.Invoke(this, req);
                    OnHashRequestReceived?.Invoke(req);

                    var tree = GetMerkleTree(req.PiecesRoot);
                    if (tree != null && req.IsValid())
                    {
                        var hashes = tree.GetHashesForRequest(req.BaseLayer, req.Index, req.Length, req.ProofLayers);
                        if (hashes != null)
                        {
                            SendHashes(new HashesMessage(req.PiecesRoot, req.BaseLayer, req.Index, req.Length, req.ProofLayers, hashes));
                            break;
                        }
                    }

                    SendHashReject(new HashRejectMessage(req));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to handle HashRequest from peer {0}:{1}", RemoteIp, RemotePort);
                }

                break;

            case PeerMessageType.Hashes:
                try
                {
                    var hashesMsg = HashesMessage.FromPeerMessage(message);
                    HashesReceived?.Invoke(this, hashesMsg);
                    OnHashesReceived?.Invoke(hashesMsg);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to handle Hashes from peer {0}:{1}", RemoteIp, RemotePort);
                }

                break;

            case PeerMessageType.HashReject:
                try
                {
                    var rej = HashRejectMessage.FromPeerMessage(message);
                    HashRejectReceived?.Invoke(this, rej);
                    OnHashRejectReceived?.Invoke(rej);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to handle HashReject from peer {0}:{1}", RemoteIp, RemotePort);
                }

                break;

            case PeerMessageType.Extended:
                if (message.Payload != null && message.Payload.Length >= 1)
                {
                    var extId = message.Payload[0];
                    var extPayload = message.Payload.Length > 1 ? message.Payload[1..] : Array.Empty<byte>();
                    HandleExtendedMessage(extId, extPayload);
                }

                break;
        }
    }

    public virtual void SendHave(int pieceIndex)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, pieceIndex);
        SendMessage(new PeerMessage { Type = PeerMessageType.Have, Payload = payload });
    }

    public virtual bool SendLtDontHave(int pieceIndex)
    {
        if (!RemoteExtensions.TryGetValue("lt_donthave", out var extId) || extId <= 0)
        {
            return false;
        }

        var payload = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(payload, pieceIndex);
        SendExtendedMessage((byte)extId, payload);
        return true;
    }

    public virtual bool HandleLtDontHave(byte[] payload)
    {
        if (payload == null || payload.Length != 4)
        {
            _logger.Debug("Ignoring invalid lt_donthave payload length from {0}:{1}", RemoteIp, RemotePort);
            return false;
        }

        var pieceIndex = BinaryPrimitives.ReadInt32BigEndian(payload);
        if (pieceIndex < 0)
        {
            _logger.Debug("Ignoring negative lt_donthave piece index {0} from {1}:{2}", pieceIndex, RemoteIp, RemotePort);
            return false;
        }

        if (_peerPieces != null && pieceIndex >= _peerPieces.Length)
        {
            _logger.Debug("Ignoring out-of-range lt_donthave piece index {0} (total {1}) from {2}:{3}", pieceIndex, _peerPieces.Length, RemoteIp, RemotePort);
            return false;
        }

        if (_bitfield != null)
        {
            var byteIndex = pieceIndex / 8;
            var bitIndex = 7 - (pieceIndex % 8);
            if (byteIndex < _bitfield.Length)
            {
                _bitfield[byteIndex] &= (byte)~(1 << bitIndex);
            }
        }

        if (_peerPieces != null && pieceIndex < _peerPieces.Length)
        {
            if (_peerPieces[pieceIndex])
            {
                _peerPieces[pieceIndex] = false;
                HaveCount = Math.Max(0, HaveCount - 1);
                if (_peerPieces.Length > 0)
                {
                    Progress = (double)HaveCount / _peerPieces.Length;
                }
            }
        }

        if (IsSeed && ((_peerPieces != null && HaveCount < _peerPieces.Length) || Progress < 1.0))
        {
            IsSeed = false;
        }

        if (InterestEvaluator != null)
        {
            IsInterested = InterestEvaluator(this);
        }
        else if (_peerPieces != null && !Array.Exists(_peerPieces, p => p))
        {
            IsInterested = false;
        }

        LtDontHaveReceived?.Invoke(pieceIndex);
        OnLtDontHaveReceived?.Invoke(pieceIndex);
        return true;
    }

    public virtual void HandleExtendedMessage(byte extensionId, byte[] payload)
    {
        if (IsLtDontHaveExtension(extensionId))
        {
            HandleLtDontHave(payload);
        }
    }

    public virtual bool IsLtDontHaveExtension(byte extensionId)
    {
        if (LocalLtDontHaveId.HasValue && extensionId == LocalLtDontHaveId.Value)
        {
            return true;
        }

        if (RemoteLtDontHaveId.HasValue && extensionId == RemoteLtDontHaveId.Value)
        {
            return true;
        }

        if (RemoteExtensions.TryGetValue("lt_donthave", out var rId) && extensionId == rId)
        {
            return true;
        }

        return false;
    }

    public void SendBitfield(int pieceCount)
    {
        if (pieceCount <= 0)
        {
            return;
        }

        if (MatchedTorrent != null && (MatchedTorrent.Status == TorrentStatus.Checking || MatchedTorrent.Status == TorrentStatus.QueuedForChecking))
        {
            var emptyByteCount = (pieceCount + 7) / 8;
            SendBitfield(new byte[emptyByteCount]);
            return;
        }

        // Send full bitfield (all pieces available - we're a seeder)
        var byteCount = (pieceCount + 7) / 8;
        var bitfield = new byte[byteCount];
        for (var i = 0; i < byteCount; i++)
        {
            bitfield[i] = 0xFF;
        }

        // Clear trailing bits in the last byte
        var spare = (byteCount * 8) - pieceCount;
        if (spare > 0)
        {
            bitfield[byteCount - 1] = (byte)(0xFF << spare);
        }

        SendBitfield(bitfield);
    }

    public static byte[] BuildHandshake(
        string infoHash,
        string peerId,
        bool isPrivate = false,
        IClientProfile clientProfile = null,
        bool supportsExtensions = true,
        bool supportsFast = true,
        bool supportsDht = true,
        bool supportsV2 = false)
    {
        var hashBytes = Convert.FromHexString(infoHash);
        if (hashBytes.Length != 20 && hashBytes.Length != 32)
        {
            throw new ArgumentException("InfoHash must be either 20 bytes (v1) or 32 bytes (v2)", nameof(infoHash));
        }

        var isV2Hash = hashBytes.Length == 32;
        var bufferSize = isV2Hash ? 80 : 68;
        var buffer = new byte[bufferSize];
        buffer[0] = 19;
        Encoding.ASCII.GetBytes(ProtocolString, 0, 19, buffer, 1);

        // Advertise BEP 10 (Extension Protocol), BEP 6 (Fast Extension), BEP 5 (DHT), BEP 52 (v2)
        var effectiveExtensions = supportsExtensions && (clientProfile == null || clientProfile.SupportsExtensionProtocol);
        if (effectiveExtensions)
        {
            buffer[25] |= 0x10; // BEP 10
        }

        var effectiveFast = supportsFast && (clientProfile == null || clientProfile.SupportsFastExtension);
        if (effectiveFast)
        {
            buffer[27] |= 0x04; // BEP 6
        }

        var effectiveDht = supportsDht && !isPrivate && (clientProfile == null || clientProfile.SupportsDht);
        if (effectiveDht)
        {
            buffer[27] |= 0x01; // BEP 5
        }

        if (supportsV2 || isV2Hash)
        {
            buffer[27] |= 0x10; // BEP 52
        }

        Array.Copy(hashBytes, 0, buffer, 28, hashBytes.Length);
        var peerIdOffset = 28 + hashBytes.Length;
        Encoding.ASCII.GetBytes(peerId.PadRight(20)[..20], 0, 20, buffer, peerIdOffset);
        return buffer;
    }

    public static void ApplySocketOptions(
        TcpClient client,
        int dscp = 0,
        int tos = 0,
        long sendRateLimit = 0,
        long receiveRateLimit = 0,
        double rttSeconds = DefaultEstimatedRttSeconds)
    {
        if (client?.Client == null)
        {
            return;
        }

        if (dscp > 0)
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, dscp << 2);
            }
            catch
            {
            }
        }
        else if (tos > 0)
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, tos);
            }
            catch
            {
            }
        }

        try
        {
            client.Client.SendBufferSize = CalculateBdpBufferSize(sendRateLimit, rttSeconds);
        }
        catch
        {
        }

        try
        {
            client.Client.ReceiveBufferSize = CalculateBdpBufferSize(receiveRateLimit, rttSeconds);
        }
        catch
        {
        }
    }

    private static void PerformSocks5Handshake(Stream stream, string targetHost, int targetPort, string username, string password)
    {
        var hasAuth = !string.IsNullOrEmpty(username);
        var greeting = hasAuth
            ? new byte[] { 0x05, 0x02, 0x00, 0x02 }
            : new byte[] { 0x05, 0x01, 0x00 };

        stream.Write(greeting, 0, greeting.Length);
        stream.Flush();

        var response = new byte[2];
        ReadExactBytes(stream, response, 0, 2);

        if (response[0] != 0x05)
        {
            throw new InvalidOperationException($"Invalid SOCKS version response from proxy: {response[0]}");
        }

        var selectedMethod = response[1];
        if (selectedMethod == 0xFF)
        {
            throw new InvalidOperationException("SOCKS5 proxy rejected all authentication methods.");
        }

        if (selectedMethod == 0x02)
        {
            var userBytes = Encoding.UTF8.GetBytes(username ?? string.Empty);
            var passBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);

            var authPayload = new byte[1 + 1 + userBytes.Length + 1 + passBytes.Length];
            authPayload[0] = 0x01;
            authPayload[1] = (byte)userBytes.Length;
            Buffer.BlockCopy(userBytes, 0, authPayload, 2, userBytes.Length);
            authPayload[2 + userBytes.Length] = (byte)passBytes.Length;
            Buffer.BlockCopy(passBytes, 0, authPayload, 3 + userBytes.Length, passBytes.Length);

            stream.Write(authPayload, 0, authPayload.Length);
            stream.Flush();

            var authResponse = new byte[2];
            ReadExactBytes(stream, authResponse, 0, 2);

            if (authResponse[1] != 0x00)
            {
                throw new InvalidOperationException("SOCKS5 proxy authentication failed.");
            }
        }

        using var ms = new MemoryStream();
        ms.WriteByte(0x05);
        ms.WriteByte(0x01); // CONNECT
        ms.WriteByte(0x00); // RSV

        if (IPAddress.TryParse(targetHost, out var ipAddress))
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                ms.WriteByte(0x01);
                var ipBytes = ipAddress.GetAddressBytes();
                ms.Write(ipBytes, 0, ipBytes.Length);
            }
            else if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                ms.WriteByte(0x04);
                var ipBytes = ipAddress.GetAddressBytes();
                ms.Write(ipBytes, 0, ipBytes.Length);
            }
            else
            {
                throw new NotSupportedException($"Address family {ipAddress.AddressFamily} is not supported by SOCKS5.");
            }
        }
        else
        {
            var domainBytes = Encoding.ASCII.GetBytes(targetHost);
            ms.WriteByte(0x03);
            ms.WriteByte((byte)domainBytes.Length);
            ms.Write(domainBytes, 0, domainBytes.Length);
        }

        ms.WriteByte((byte)((targetPort >> 8) & 0xFF));
        ms.WriteByte((byte)(targetPort & 0xFF));

        var connectPayload = ms.ToArray();
        stream.Write(connectPayload, 0, connectPayload.Length);
        stream.Flush();

        var connectHeader = new byte[4];
        ReadExactBytes(stream, connectHeader, 0, 4);

        if (connectHeader[1] != 0x00)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        var atyp = connectHeader[3];
        var addrLen = atyp switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => stream.ReadByte(),
            _ => throw new InvalidOperationException($"Unknown SOCKS5 ATYP: {atyp}")
        };

        if (addrLen <= 0)
        {
            throw new InvalidOperationException("SOCKS5 proxy returned invalid address length.");
        }

        var boundAddress = new byte[addrLen + 2];
        ReadExactBytes(stream, boundAddress, 0, boundAddress.Length);
    }

    private static void PerformHttpConnectHandshake(Stream stream, string targetHost, int targetPort, string username, string password)
    {
        var formattedHost = targetHost.Contains(':') && !targetHost.StartsWith('[') ? $"[{targetHost}]" : targetHost;
        var sb = new StringBuilder();
        sb.Append($"CONNECT {formattedHost}:{targetPort} HTTP/1.1\r\n");
        sb.Append($"Host: {formattedHost}:{targetPort}\r\n");

        if (!string.IsNullOrEmpty(username))
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password ?? string.Empty}"));
            sb.Append($"Proxy-Authorization: Basic {auth}\r\n");
        }

        sb.Append("\r\n");

        var requestBytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(requestBytes, 0, requestBytes.Length);
        stream.Flush();

        using var headerMs = new MemoryStream();
        var buffer = new byte[1];
        var headersComplete = false;

        while (headerMs.Length < 8192)
        {
            var read = stream.Read(buffer, 0, 1);
            if (read == 0)
            {
                throw new IOException("HTTP CONNECT proxy closed connection during handshake");
            }

            headerMs.WriteByte(buffer[0]);
            var len = headerMs.Length;
            if (len >= 4)
            {
                var bytes = headerMs.GetBuffer();
                if (bytes[len - 4] == '\r' &&
                    bytes[len - 3] == '\n' &&
                    bytes[len - 2] == '\r' &&
                    bytes[len - 1] == '\n')
                {
                    headersComplete = true;
                    break;
                }
            }
        }

        if (!headersComplete)
        {
            throw new InvalidOperationException("HTTP CONNECT proxy response headers exceeded 8192 bytes without terminating delimiter.");
        }

        var responseText = Encoding.ASCII.GetString(headerMs.ToArray());
        if (!responseText.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase) &&
            !responseText.StartsWith("HTTP/1.0 200", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"HTTP CONNECT proxy returned non-200 status: {responseText.Split(new[] { "\r\n" }, StringSplitOptions.None)[0]}");
        }
    }

    private static void ReadExactBytes(Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
            {
                throw new EndOfStreamException("Proxy closed the connection unexpectedly.");
            }

            totalRead += read;
        }
    }

    private static bool IsTimeoutException(Exception ex)
    {
        return (ex is SocketException se && se.SocketErrorCode == SocketError.TimedOut) ||
            (ex is IOException io && ((io.InnerException is SocketException innerSe && innerSe.SocketErrorCode == SocketError.TimedOut) || io.InnerException is TimeoutException)) ||
            ex is TimeoutException;
    }

    internal async ValueTask<bool> ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        var count = buffer.Length;
        var chunkSize = _pacingChunkSize > 0 ? _pacingChunkSize : DefaultPacingChunkSize;
        var hasDownloadLimit = _downloadRateLimit > 0 || (BandwidthLimiter != null && BandwidthLimiter.HasDownloadLimit(InfoHash, PeerId));

        while (offset < count)
        {
            var toRead = count - offset;
            if (hasDownloadLimit)
            {
                toRead = Math.Min(toRead, chunkSize);
                if (BandwidthLimiter != null && BandwidthLimiter.HasDownloadLimit(InfoHash, PeerId))
                {
                    BandwidthLimiter.ConsumeDownload(InfoHash, PeerId, toRead);
                }
                else if (_downloadRateLimit > 0)
                {
                    var targetTicks = (long)((double)toRead * Stopwatch.Frequency / _downloadRateLimit);
                    PaceWaitHandler(Stopwatch.GetTimestamp(), targetTicks);
                }
            }

            var read = await _activeStream.ReadAsync(buffer.Slice(offset, toRead), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
            _bytesReadForCurrentMessage += read;
        }

        return true;
    }

    private bool ReadExact(byte[] buffer, int count)
    {
        var bytesRead = 0;
        return ReadExact(buffer, count, ref bytesRead);
    }

    private bool ReadExact(byte[] buffer, int count, ref int bytesReadForMessage)
    {
        var offset = 0;
        var chunkSize = _pacingChunkSize > 0 ? _pacingChunkSize : DefaultPacingChunkSize;
        var hasDownloadLimit = _downloadRateLimit > 0 || (BandwidthLimiter != null && BandwidthLimiter.HasDownloadLimit(InfoHash, PeerId));

        while (offset < count)
        {
            var toRead = count - offset;
            if (hasDownloadLimit)
            {
                toRead = Math.Min(toRead, chunkSize);
                if (BandwidthLimiter != null && BandwidthLimiter.HasDownloadLimit(InfoHash, PeerId))
                {
                    BandwidthLimiter.ConsumeDownload(InfoHash, PeerId, toRead);
                }
                else if (_downloadRateLimit > 0)
                {
                    var targetTicks = (long)((double)toRead * Stopwatch.Frequency / _downloadRateLimit);
                    PaceWaitHandler(Stopwatch.GetTimestamp(), targetTicks);
                }
            }

            var read = _activeStream.Read(buffer, offset, toRead);
            if (read == 0)
            {
                return false;
            }

            offset += read;
            bytesReadForMessage += read;
        }

        return true;
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            PendingRequestCount = 0;

            if (BandwidthLimiter != null && !string.IsNullOrEmpty(_peerId))
            {
                BandwidthLimiter.RemovePeer(_peerId);
            }

            if (_activeStream != _networkStream)
            {
                _activeStream?.Dispose();
            }

            _networkStream?.Dispose();
            _client?.Dispose();
        }
    }
}

public class PeerPexTracker
{
    public DateTime LastPexSent { get; set; } = DateTime.MinValue;
    public HashSet<string> PreviouslySentPeers { get; } = new(StringComparer.OrdinalIgnoreCase);
}
