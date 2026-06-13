using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public interface IClientProfile : IProvider
{
    string PeerIdPrefix { get; }
    string UserAgent { get; }
    string ClientVersion { get; }
    int DefaultPort { get; }
    bool SupportsEncryption { get; }
    bool SupportsDht { get; }
    bool SupportsPex { get; }
    string GeneratePeerId();
}
