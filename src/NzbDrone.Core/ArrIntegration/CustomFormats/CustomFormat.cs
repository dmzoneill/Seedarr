using System.Collections.Generic;

namespace NzbDrone.Core.ArrIntegration.CustomFormats;

public class CustomFormat
{
    public int Id { get; set; }

    public string Name { get; set; }

    public bool IncludeCustomFormatWhenRenaming { get; set; }

    public List<CustomFormatSpecification> Specifications { get; set; } = new();
}
