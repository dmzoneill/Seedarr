namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class DelugeProfile : IClientProfile
{
    public const string CharacterSet = PeerIdGenerator.UrlSafeCharacterSet;

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
        return PeerIdGenerator.Generate(PeerIdPrefix, CharacterSet);
    }
}
