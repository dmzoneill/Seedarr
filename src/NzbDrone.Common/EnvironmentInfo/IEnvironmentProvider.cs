namespace NzbDrone.Common.EnvironmentInfo;

public interface IEnvironmentProvider
{
    bool IsDocker { get; }
    string GetEnvironmentVariable(string variable);
}
