using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ThingiProvider;

public class ProviderDefinition : ModelBase
{
    public string Name { get; set; }
    public string Implementation { get; set; }
    public string ConfigContract { get; set; }
    public string Settings { get; set; }
    public bool Enable { get; set; }
    public int Priority { get; set; }
}
