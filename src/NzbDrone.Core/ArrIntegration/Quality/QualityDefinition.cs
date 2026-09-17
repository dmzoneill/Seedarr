namespace NzbDrone.Core.ArrIntegration.Quality;

public class QualityDefinition
{
    public int QualityId { get; set; }

    public string Title { get; set; }

    public double? MinSize { get; set; }

    public double? MaxSize { get; set; }

    public double? PreferredSize { get; set; }
}
