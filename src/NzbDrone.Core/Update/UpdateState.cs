using System;

namespace NzbDrone.Core.Update;

public enum UpdateLifecycleState
{
    Idle = 0,
    Downloading = 1,
    Staging = 2,
    Restarting = 3,
    PendingVerification = 4,
    Verifying = 4,
    Completed = 5,
    RolledBack = 6,
}

public record UpdateState
{
    public static UpdateLifecycleState Idle => UpdateLifecycleState.Idle;

    public static UpdateLifecycleState Downloading => UpdateLifecycleState.Downloading;

    public static UpdateLifecycleState Staging => UpdateLifecycleState.Staging;

    public static UpdateLifecycleState Restarting => UpdateLifecycleState.Restarting;

    public static UpdateLifecycleState PendingVerification => UpdateLifecycleState.PendingVerification;

    public static UpdateLifecycleState Verifying => UpdateLifecycleState.Verifying;

    public static UpdateLifecycleState Completed => UpdateLifecycleState.Completed;

    public static UpdateLifecycleState RolledBack => UpdateLifecycleState.RolledBack;

    public string PreviousVersion { get; init; } = string.Empty;

    public string TargetVersion { get; init; } = string.Empty;

    public UpdateLifecycleState State { get; init; } = UpdateLifecycleState.Idle;

    public DateTime? InitiatedAt { get; init; }

    public int VerificationTimeoutSeconds { get; init; } = 60;

    public string BackupDirectory { get; init; } = string.Empty;

    public string ErrorMessage { get; init; }

    public UpdateState()
    {
    }

    public UpdateState(
        string previousVersion,
        string targetVersion,
        UpdateLifecycleState state = UpdateLifecycleState.Idle,
        DateTime? initiatedAt = null,
        int verificationTimeoutSeconds = 60,
        string backupDirectory = null,
        string errorMessage = null)
    {
        PreviousVersion = previousVersion ?? string.Empty;
        TargetVersion = targetVersion ?? string.Empty;
        State = state;
        InitiatedAt = initiatedAt;
        VerificationTimeoutSeconds = verificationTimeoutSeconds;
        BackupDirectory = backupDirectory ?? string.Empty;
        ErrorMessage = errorMessage;
    }

    public void Deconstruct(
        out string previousVersion,
        out string targetVersion,
        out UpdateLifecycleState state,
        out DateTime? initiatedAt,
        out int verificationTimeoutSeconds,
        out string backupDirectory,
        out string errorMessage)
    {
        previousVersion = PreviousVersion;
        targetVersion = TargetVersion;
        state = State;
        initiatedAt = InitiatedAt;
        verificationTimeoutSeconds = VerificationTimeoutSeconds;
        backupDirectory = BackupDirectory;
        errorMessage = ErrorMessage;
    }
}
