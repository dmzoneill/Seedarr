using System.Security.Cryptography;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class UTorrentProfile : IClientProfile
{
    public string Name => "uTorrent 3.5.5";
    public string PeerIdPrefix => "-UT3550-";
    public string UserAgent => "uTorrent/3.5.5";
    public string ClientVersion => "3.5.5";
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
