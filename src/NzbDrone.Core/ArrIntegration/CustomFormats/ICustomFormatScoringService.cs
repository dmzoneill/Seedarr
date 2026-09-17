using System.Collections.Generic;

namespace NzbDrone.Core.ArrIntegration.CustomFormats;

public interface ICustomFormatScoringService
{
    CustomFormatScoreResult Evaluate(string releaseTitle, IEnumerable<CustomFormat> customFormats, IDictionary<int, int> profileScores);

    CustomFormatScoreResult Evaluate(string releaseTitle, IEnumerable<CustomFormat> customFormats);
}
