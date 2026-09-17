namespace NzbDrone.Core.Notifications;

public class CustomScriptTestResult
{
    public bool Success { get; set; }

    public int ExitCode { get; set; }

    public string Stdout { get; set; } = string.Empty;

    public string Stderr { get; set; } = string.Empty;

    public long ExecutionTimeMs { get; set; }

    public bool TimedOut { get; set; }

    public string ResolvedInterpreter { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;
}
