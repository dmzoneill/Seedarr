using System.Security.Cryptography;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class TransmissionProfile : IClientProfile
{
    public string Name => "Transmission 3.00";
    public string PeerIdPrefix => "-TR3000-";
    public string UserAgent => "Transmission/3.00";
    public string ClientVersion => "3.00";
    public int DefaultPort => 51413;
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
