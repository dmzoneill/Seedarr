using System.Collections.Generic;

namespace Seedarr.Api.V1.Tags;

public class BulkTagRequest
{
    public List<int> TagIds { get; set; } = new();
    public List<int> TorrentIds { get; set; } = new();
}
