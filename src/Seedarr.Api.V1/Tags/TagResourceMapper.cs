using NzbDrone.Core.Tags;

namespace Seedarr.Api.V1.Tags;

public static class TagResourceMapper
{
    public static TagResource ToResource(Tag model, int torrentCount = 0)
    {
        if (model == null)
        {
            return null;
        }

        return new TagResource
        {
            Id = model.Id,
            Label = model.Label,
            Color = model.Color,
            UploadLimitKbps = model.UploadLimitKbps,
            DownloadLimitKbps = model.DownloadLimitKbps,
            MinSeedRatio = model.MinSeedRatio,
            MinSeedTimeSeconds = model.MinSeedTimeSeconds,
            TorrentCount = torrentCount
        };
    }

    public static Tag ToModel(TagResource resource)
    {
        if (resource == null)
        {
            return null;
        }

        return new Tag
        {
            Id = resource.Id,
            Label = resource.Label,
            Color = resource.Color,
            UploadLimitKbps = resource.UploadLimitKbps,
            DownloadLimitKbps = resource.DownloadLimitKbps,
            MinSeedRatio = resource.MinSeedRatio,
            MinSeedTimeSeconds = resource.MinSeedTimeSeconds
        };
    }
}
