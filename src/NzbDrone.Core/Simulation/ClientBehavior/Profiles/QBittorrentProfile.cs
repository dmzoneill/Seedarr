namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class QBittorrentProfile : IClientProfile
{
    public const string CharacterSet = PeerIdGenerator.UrlSafeCharacterSet;

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
        return PeerIdGenerator.Generate(PeerIdPrefix, CharacterSet);
    }
}
