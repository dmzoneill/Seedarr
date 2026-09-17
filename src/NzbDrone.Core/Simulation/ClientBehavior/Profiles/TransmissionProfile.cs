using System.Collections.Generic;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public class TransmissionProfile : IClientProfile
{
    public const string CharacterSet = PeerIdGenerator.Base62CharacterSet;

    public string Name => "Transmission 3.00";
    public string PeerIdPrefix => "-TR3000-";
    public string UserAgent => "Transmission/3.00";
    public string ClientVersion => "3.00";
    public int DefaultPort => 51413;
    public bool SupportsEncryption => true;
    public bool SupportsDht => true;
    public bool SupportsPex => true;

    public IReadOnlyList<string> AnnounceParameterOrder => new[]
    {
        "info_hash", "peer_id", "port", "uploaded", "downloaded", "left",
        "numwant", "key", "compact", "supportcrypto", "event"
    };

    public IDictionary<string, string> ExtraAnnounceParameters => new Dictionary<string, string>
    {
        { "supportcrypto", "1" }
    };

    public string GeneratePeerId()
    {
        return PeerIdGenerator.Generate(PeerIdPrefix, CharacterSet);
    }
}
