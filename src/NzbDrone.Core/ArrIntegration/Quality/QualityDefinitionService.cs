namespace NzbDrone.Core.ArrIntegration.Quality;

public class QualityDefinitionService : IQualityDefinitionService
{
    private const int DefaultRuntimeMinutes = 90;
    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    public bool IsWithinSizeBoundary(long sizeBytes, int runtimeMinutes, QualityDefinition definition)
    {
        return IsWithinSizeBoundary(sizeBytes, (int?)runtimeMinutes, definition);
    }

    public bool IsWithinSizeBoundary(long sizeBytes, int? runtimeMinutes, QualityDefinition definition)
    {
        if (definition == null)
        {
            return true;
        }

        if (sizeBytes < 0)
        {
            return false;
        }

        var effectiveRuntime = runtimeMinutes.HasValue && runtimeMinutes.Value > 0
            ? runtimeMinutes.Value
            : DefaultRuntimeMinutes;

        if (definition.MinSize.HasValue && definition.MinSize.Value > 0)
        {
            var minBytes = (long)(definition.MinSize.Value * effectiveRuntime * BytesPerMegabyte);
            if (sizeBytes < minBytes)
            {
                return false;
            }
        }

        if (definition.MaxSize.HasValue && definition.MaxSize.Value > 0)
        {
            var maxBytes = (long)(definition.MaxSize.Value * effectiveRuntime * BytesPerMegabyte);
            if (sizeBytes > maxBytes)
            {
                return false;
            }
        }

        return true;
    }
}
