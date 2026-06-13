using System.Security.Cryptography;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class DelugeProfile : IClientProfile
{
    public string Name => "Deluge 2.0.3";
    public string PeerIdPrefix => "-DE2030-";
    public string UserAgent => "Deluge/2.0.3";
    public string ClientVersion => "2.0.3";
    public int DefaultPort => 6881;
    public bool SupportsEncryption => true;
    public bool SupportsDht => true;
    public bool SupportsPex => true;

    public string GeneratePeerId()
    {
        var suffix = new byte[12];
        RandomNumberGenerator.Fill(suffix);
        var chars = new char[12];
        for (var i = 0; i < 12; i++)
        {
            chars[i] = (char)('0' + (suffix[i] % 10));
        }

        return PeerIdPrefix + new string(chars);
    }
}
