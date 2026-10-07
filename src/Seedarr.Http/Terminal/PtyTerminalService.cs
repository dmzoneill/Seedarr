// Copyright (c) FeedItOut. All rights reserved.

#pragma warning disable SX1309

using System;
using System.IO;
using System.Security;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Terminal;

namespace Seedarr.Http.Terminal;

public class PtyTerminalService : IPtyTerminalService
{
    private readonly IConfigFileProvider _configFileProvider;
    private readonly IConfigService _configService;

    public PtyTerminalService(IConfigFileProvider configFileProvider = null, IConfigService configService = null)
    {
        this._configFileProvider = configFileProvider;
        this._configService = configService;
    }

    public ITerminalSession CreateSession(string cwd, int cols, int rows)
    {
        // 1. Validate security configuration / permissions
        if (!this.IsTerminalAccessPermitted())
        {
            throw new SecurityException("Terminal process execution is prohibited by security configuration.");
        }

        string sanitizedCwd = TerminalCwdResolver.ResolveWorkingDirectory(cwd, _configService);

        // 3. Clamp dimensions to safe bounds
        var (clampedCols, clampedRows) = TerminalGeometry.Clamp(cols, rows);

        if (OperatingSystem.IsLinux())
        {
            try
            {
                return LinuxPtySession.Start(sanitizedCwd, clampedCols, clampedRows);
            }
            catch
            {
                // Fall back to python PTY or standard process session
            }
        }

        if (File.Exists("/usr/bin/python3") || File.Exists("/bin/python3") || File.Exists("/usr/local/bin/python3"))
        {
            try
            {
                return PtyProcessSession.Start(sanitizedCwd, clampedCols, clampedRows);
            }
            catch
            {
                // Fall back to standard process session
            }
        }

        return FallbackProcessSession.Start(sanitizedCwd, clampedCols, clampedRows);
    }

    public bool IsTerminalAccessPermitted()
    {
        return this._configFileProvider?.TerminalAccessEnabled == true;
    }

}
