using System;
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

        byte[] signatureBytes;
        byte[] publicKeyBytes;

        try
        {
            signatureBytes = Convert.FromHexString(signatureHex.Trim());
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

            return signer.VerifySignature(signatureBytes);
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

        var now = DateTimeOffset.UtcNow;
        var difference = (now - requestTime).Duration();

        return difference <= MaxRequestAge;
    }
}
