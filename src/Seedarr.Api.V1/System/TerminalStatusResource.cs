using System.Collections.Generic;

namespace Seedarr.Api.V1.System;

public class TerminalStatusResource
{
    public bool TerminalAccessEnabled { get; set; }
    public IReadOnlyCollection<string> ActiveSessions { get; set; }
}
