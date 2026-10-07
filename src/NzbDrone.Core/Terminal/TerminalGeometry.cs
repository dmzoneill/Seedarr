// Copyright (c) FeedItOut. All rights reserved.

using System;

namespace NzbDrone.Core.Terminal;

public static class TerminalGeometry
{
    public const int MinCols = 10;
    public const int MaxCols = 500;
    public const int MinRows = 5;
    public const int MaxRows = 200;

    public static (int Cols, int Rows) Clamp(int cols, int rows)
    {
        int clampedCols = Math.Clamp(cols, MinCols, MaxCols);
        int clampedRows = Math.Clamp(rows, MinRows, MaxRows);
        return (clampedCols, clampedRows);
    }

    public static (int Cols, int Rows) NormalizeSignalRDimensions(int cols, int rows)
    {
        cols = cols <= 0 ? 80 : cols;
        rows = rows <= 0 ? 24 : rows;
        return Clamp(cols, rows);
    }
}
