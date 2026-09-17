namespace NzbDrone.Core.Notifications.Discord;

public interface IDiscordSecurityService
{
    bool VerifySignature(string signatureHex, string timestamp, byte[] bodyBytes, string publicKeyHex);

    string GenerateSignature(string privateKeyHex, string timestamp, byte[] bodyBytes);
}
