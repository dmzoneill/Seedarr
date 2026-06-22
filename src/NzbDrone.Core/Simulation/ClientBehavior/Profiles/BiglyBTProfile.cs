using System.Security.Cryptography;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class BiglyBTProfile : IClientProfile
{
    public string Name => "BiglyBT 2.7.0.0";
    public string PeerIdPrefix => "-BG2700-";
    public string UserAgent => "BiglyBT/2.7.0.0";
    public string ClientVersion => "2.7.0.0";
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
