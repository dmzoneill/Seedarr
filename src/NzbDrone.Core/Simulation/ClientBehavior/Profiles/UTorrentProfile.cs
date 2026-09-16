namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class UTorrentProfile : IClientProfile
{
    public const string CharacterSet = PeerIdGenerator.Base62CharacterSet;

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
        return PeerIdGenerator.Generate(PeerIdPrefix, CharacterSet);
    }
}
