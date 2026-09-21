namespace NzbDrone.Core.Terminal;

public static class TerminalDimensionValidator
{
    public const int MinCols = 10;
    public const int MaxCols = 500;
    public const int MinRows = 5;
    public const int MaxRows = 200;

    public static bool IsValid(int cols, int rows)
    {
        return cols >= MinCols && cols <= MaxCols && rows >= MinRows && rows <= MaxRows;
    }

    public static bool Validate(int cols, int rows, out string errorMessage)
    {
        if (cols < MinCols || cols > MaxCols)
        {
            errorMessage = $"Terminal columns ({cols}) must be between {MinCols} and {MaxCols}.";
            return false;
        }

        if (rows < MinRows || rows > MaxRows)
        {
            errorMessage = $"Terminal rows ({rows}) must be between {MinRows} and {MaxRows}.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
