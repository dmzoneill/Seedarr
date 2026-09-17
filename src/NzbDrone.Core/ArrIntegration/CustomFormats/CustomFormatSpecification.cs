using System.Collections.Generic;

namespace NzbDrone.Core.ArrIntegration.CustomFormats;

public class CustomFormatSpecification
{
    public string Name { get; set; }

    public string Implementation { get; set; }

    public bool Negate { get; set; }

    public bool Required { get; set; }

    public Dictionary<string, string> Fields { get; set; } = new();
}
