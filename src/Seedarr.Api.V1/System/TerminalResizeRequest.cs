namespace Seedarr.Api.V1.System;

public class TerminalResizeRequest
{
    public string ConnectionId { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
}
