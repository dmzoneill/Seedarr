using System.Security.Cryptography;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class QBittorrentProfile : IClientProfile
{
    public string Name => "qBittorrent 4.4.2";
    public string PeerIdPrefix => "-qB4420-";
    public string UserAgent => "qBittorrent/4.4.2";
    public string ClientVersion => "4.4.2";
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
