// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NzbDrone.Core.Terminal;

namespace Seedarr.Http.Terminal;

public static class FallbackTerminalGeometry
{
    public const int MinCols = TerminalGeometry.MinCols;
    public const int MaxCols = TerminalGeometry.MaxCols;
    public const int MinRows = TerminalGeometry.MinRows;
    public const int MaxRows = TerminalGeometry.MaxRows;

    public static (int Cols, int Rows) Clamp(int cols, int rows) => TerminalGeometry.Clamp(cols, rows);

    public static void ApplyToEnvironment(ProcessStartInfo startInfo, int cols, int rows)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var (clampedCols, clampedRows) = Clamp(cols, rows);
        startInfo.Environment["LINES"] = clampedRows.ToString();
        startInfo.Environment["COLUMNS"] = clampedCols.ToString();
    }

    public static byte[] BuildResizePayload(int cols, int rows)
    {
        var (clampedCols, clampedRows) = Clamp(cols, rows);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Encoding.UTF8.GetBytes(
                $"$env:LINES={clampedRows}; $env:COLUMNS={clampedCols}\n");
        }

        return Encoding.UTF8.GetBytes(
            $"export LINES={clampedRows} COLUMNS={clampedCols}; stty rows {clampedRows} cols {clampedCols} 2>/dev/null\n");
    }
}
