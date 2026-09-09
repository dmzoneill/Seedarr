using System;
using System.IO;

namespace NzbDrone.Common.EnvironmentInfo;

public interface IAppFolderInfo
{
    string AppDataFolder { get; }
    string StartUpFolder { get; }
}

public class AppFolderInfo : IAppFolderInfo
{
    public AppFolderInfo(StartupContext startupContext)
    {
        if (startupContext.Args.TryGetValue("data", out var dataDir))
        {
            AppDataFolder = dataDir;
        }
        else
        {
            var appDataParent = OsInfo.IsWindows
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            AppDataFolder = Path.Combine(appDataParent, "Seedarr");
        }

        StartUpFolder = AppDomain.CurrentDomain.BaseDirectory;

        Directory.CreateDirectory(AppDataFolder);
    }

    public string AppDataFolder { get; }
    public string StartUpFolder { get; }
}
