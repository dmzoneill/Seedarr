using System;
using NLog;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.MediaEnrichment;

public class MediaCoverMaintenanceTask : IScheduledTask
{
    private readonly IMediaEnrichmentService _enrichmentService;
    private readonly Logger _logger;

    public int DefaultInterval => 1440;

    public MediaCoverMaintenanceTask(IMediaEnrichmentService enrichmentService)
    {
        _enrichmentService = enrichmentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        try
        {
            _logger.Info("Starting MediaCover cache maintenance and LRU eviction");
            _enrichmentService.EvictMediaCoverCache();
            _logger.Info("MediaCover cache maintenance completed");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to execute MediaCover maintenance task");
        }
    }
}
