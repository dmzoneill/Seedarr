using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NzbDrone.Core.Notifications.Discord;

public class DiscordSecurityService : IDiscordSecurityService
{
    public const int SignatureLengthBytes = 64;
    public const int PublicKeyLengthBytes = 32;
    public static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _replayCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _purgeLock = new();
    private DateTimeOffset _lastPurgeTime = DateTimeOffset.UtcNow;

    public int ReplayCacheCount => _replayCache.Count;

    public void ClearReplayCache()
    {
        _replayCache.Clear();
    }

    public int PurgeExpired()
    {
        lock (_purgeLock)
        {
            return PurgeExpiredInternal(DateTimeOffset.UtcNow);
        }
    }

    private int PurgeExpiredInternal(DateTimeOffset now)
    {
        _lastPurgeTime = now;
        var count = 0;
        foreach (var kvp in _replayCache)
        {
            if (kvp.Value <= now)
            {
                if (_replayCache.TryRemove(kvp.Key, out _))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void CleanupExpiredEntriesIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastPurgeTime < TimeSpan.FromMinutes(1) && _replayCache.Count < 1000)
        {
            return;
        }

        lock (_purgeLock)
        {
            if (now - _lastPurgeTime < TimeSpan.FromMinutes(1) && _replayCache.Count < 1000)
            {
                return;
            }

            PurgeExpiredInternal(now);
        }
    }

    private bool IsReplayed(string signatureHex)
    {
        if (_replayCache.TryGetValue(signatureHex, out var expiresAt))
        {
            if (expiresAt > DateTimeOffset.UtcNow)
            {
                return true;
            }

            _replayCache.TryRemove(signatureHex, out _);
        }

        return false;
    }

    public bool VerifySignature(string signatureHex, string timestamp, byte[] bodyBytes, string publicKeyHex)
    {
        if (string.IsNullOrWhiteSpace(signatureHex) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(publicKeyHex))
        {
            return false;
        }

        if (!IsTimestampFresh(timestamp))
        {
            return false;
        }

        var normalizedSignature = signatureHex.Trim();

        byte[] signatureBytes;
        byte[] publicKeyBytes;

        try
        {
            signatureBytes = Convert.FromHexString(normalizedSignature);
            publicKeyBytes = Convert.FromHexString(publicKeyHex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (signatureBytes.Length != SignatureLengthBytes || publicKeyBytes.Length != PublicKeyLengthBytes)
        {
            return false;
        }

        if (IsReplayed(normalizedSignature))
        {
            return false;
        }

        try
        {
            var pubKeyParams = new Ed25519PublicKeyParameters(publicKeyBytes, 0);
            var signer = new Ed25519Signer();
            signer.Init(false, pubKeyParams);

            var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
            signer.BlockUpdate(timestampBytes, 0, timestampBytes.Length);

            if (bodyBytes != null && bodyBytes.Length > 0)
            {
                signer.BlockUpdate(bodyBytes, 0, bodyBytes.Length);
            }

            if (!signer.VerifySignature(signatureBytes))
            {
                return false;
            }

            var expiresAt = DateTimeOffset.UtcNow + MaxRequestAge + MaxClockSkew;
            if (!_replayCache.TryAdd(normalizedSignature, expiresAt))
            {
                return false;
            }

            CleanupExpiredEntriesIfDue();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GenerateSignature(string privateKeyHex, string timestamp, byte[] bodyBytes)
    {
        if (string.IsNullOrWhiteSpace(privateKeyHex) || string.IsNullOrWhiteSpace(timestamp))
        {
            throw new ArgumentException("Private key and timestamp must be provided");
        }

        var privateKeyBytes = Convert.FromHexString(privateKeyHex.Trim());
        var privKeyParams = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privKeyParams);

        var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
        signer.BlockUpdate(timestampBytes, 0, timestampBytes.Length);

        if (bodyBytes != null && bodyBytes.Length > 0)
        {
            signer.BlockUpdate(bodyBytes, 0, bodyBytes.Length);
        }

        var signature = signer.GenerateSignature();
        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    private static bool IsTimestampFresh(string timestamp)
    {
        DateTimeOffset requestTime;

        try
        {
            if (long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTime))
            {
                requestTime = unixTime > 9999999999L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unixTime)
                    : DateTimeOffset.FromUnixTimeSeconds(unixTime);
            }
            else if (DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDt))
            {
                requestTime = parsedDt;
            }
            else
            {
                return false;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        if (requestTime > now + MaxClockSkew)
        {
            return false;
        }

        if (requestTime < now - MaxRequestAge)
        {
            return false;
        }

        return true;
    }
}
