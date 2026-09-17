namespace Seedarr.Api.V1.System;

public class SetupStatusResource
{
    public bool IsSetupCompleted { get; set; }

    public bool IsAuthEnabled { get; set; }

    public bool HasAdminUser { get; set; }
}
