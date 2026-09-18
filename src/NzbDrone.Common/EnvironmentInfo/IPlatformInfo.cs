namespace NzbDrone.Common.EnvironmentInfo;

public interface IPlatformInfo
{
    string Platform { get; }
    bool IsDocker { get; }
}
