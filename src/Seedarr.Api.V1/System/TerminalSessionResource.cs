using System;

namespace Seedarr.Api.V1.System;

public class TerminalSessionResource
{
    public string ConnectionId { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public DateTime CreatedAt { get; set; }
}
