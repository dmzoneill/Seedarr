using System;
using NzbDrone.Core.Update;

namespace Seedarr.Api.V1.Update;

public class UpdateStatusResource
{
    public string PreviousVersion { get; set; }

    public string TargetVersion { get; set; }

    public UpdateLifecycleState State { get; set; }

    public DateTime? InitiatedAt { get; set; }

    public int VerificationTimeoutSeconds { get; set; }

    public string BackupDirectory { get; set; }

    public string ErrorMessage { get; set; }

    public string Mechanism { get; set; }

    public string PackageUrl { get; set; }

    public string PackageFileName { get; set; }

    public string ReleaseChannel { get; set; }
}
