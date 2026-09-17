using System.Collections.Generic;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractionResult
{
    public bool Success { get; set; }

    public List<string> ExtractedFiles { get; set; } = new();

    public string ErrorMessage { get; set; }

    public string DestinationPath { get; set; }
}
