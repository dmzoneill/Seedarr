using System.Collections.Generic;

namespace NzbDrone.Core.ArrIntegration.CustomFormats;

public class CustomFormatScoreResult
{
    public int TotalScore { get; set; }

    public List<string> MatchedFormats { get; set; } = new();
}
