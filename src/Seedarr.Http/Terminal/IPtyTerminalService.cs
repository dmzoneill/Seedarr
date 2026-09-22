// Copyright (c) PlaceholderCompany. All rights reserved.

namespace Seedarr.Http.Terminal;

public interface IPtyTerminalService
{
    ITerminalSession CreateSession(string cwd, int cols, int rows);
}
