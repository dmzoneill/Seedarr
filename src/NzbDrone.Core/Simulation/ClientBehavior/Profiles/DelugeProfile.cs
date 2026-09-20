using System.Collections.Generic;

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
    public bool SupportsExtensionProtocol => true;
    public bool SupportsFastExtension => true;

    public IReadOnlyList<string> AnnounceParameterOrder => new[]
    {
        "info_hash", "peer_id", "port", "uploaded", "downloaded", "left",
        "corrupt", "key", "event", "numwant", "compact", "no_peer_id",
        "supportcrypto", "redundant"
    };

    public IDictionary<string, string> ExtraAnnounceParameters => new Dictionary<string, string>
    {
        { "corrupt", "0" },
        { "no_peer_id", "1" },
        { "supportcrypto", "1" },
        { "redundant", "0" }
    };

    public string GeneratePeerId()
    {
        return PeerIdGenerator.Generate(PeerIdPrefix, CharacterSet);
    }
}
