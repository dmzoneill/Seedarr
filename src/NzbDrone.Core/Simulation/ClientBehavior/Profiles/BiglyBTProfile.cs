using System.Collections.Generic;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class BiglyBTProfile : IClientProfile
{
    public const string CharacterSet = PeerIdGenerator.Base62CharacterSet;

    public string Name => "BiglyBT 2.7.0.0";
    public string PeerIdPrefix => "-BI2700-";
    public string UserAgent => "BiglyBT/2.7.0.0";
    public string ClientVersion => "2.7.0.0";
    public int DefaultPort => 6881;
    public bool SupportsEncryption => true;
    public bool SupportsDht => true;
    public bool SupportsPex => true;
    public bool SupportsExtensionProtocol => true;
    public bool SupportsFastExtension => true;

    public IReadOnlyList<string> AnnounceParameterOrder => new[]
    {
        "info_hash", "peer_id", "port", "uploaded", "downloaded", "left",
        "numwant", "key", "compact", "event"
    };

    public IDictionary<string, string> ExtraAnnounceParameters => new Dictionary<string, string>();

    public string GeneratePeerId()
    {
        return PeerIdGenerator.Generate(PeerIdPrefix, CharacterSet);
    }
}
