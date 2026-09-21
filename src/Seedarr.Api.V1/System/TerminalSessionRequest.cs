namespace Seedarr.Api.V1.System;

public class TerminalSessionRequest
{
    public string ConnectionId { get; set; }
    public int Cols { get; set; } = 80;
    public int Rows { get; set; } = 24;
}
