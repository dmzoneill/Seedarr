using System.Collections.Generic;

namespace Seedarr.Api.V1.Torrents;

public class SetFilePriorityRequest
{
    public int? Priority { get; set; }
}

public class SetFilePriorityItem
{
    public int FileId { get; set; }

    public int Priority { get; set; }
}

public class SetFilePrioritiesRequest
{
    public List<SetFilePriorityItem> Files { get; set; } = new();
}
