namespace NzbDrone.Core.ArrIntegration.Quality;

public interface IQualityDefinitionService
{
    bool IsWithinSizeBoundary(long sizeBytes, int runtimeMinutes, QualityDefinition definition);

    bool IsWithinSizeBoundary(long sizeBytes, int? runtimeMinutes, QualityDefinition definition);
}
