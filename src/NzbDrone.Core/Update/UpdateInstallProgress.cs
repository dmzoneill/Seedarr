namespace NzbDrone.Core.Update;

public enum UpdateInstallStage
{
    Idle = 0,
    Downloading = 1,
    Verifying = 2,
    Extracting = 3,
    Installing = 4,
    RestartRequired = 5,
    Failed = 6,
}

public class UpdateInstallProgress
{
    public UpdateInstallStage Stage { get; set; } = UpdateInstallStage.Idle;

    public int Percentage { get; set; } = 0;

    public string ErrorMessage { get; set; }

    public string TargetVersion { get; set; }

    public UpdateInstallProgress()
    {
    }

    public UpdateInstallProgress(UpdateInstallStage stage, int percentage, string errorMessage = null, string targetVersion = null)
    {
        Stage = stage;
        Percentage = percentage;
        ErrorMessage = errorMessage;
        TargetVersion = targetVersion;
    }
}
