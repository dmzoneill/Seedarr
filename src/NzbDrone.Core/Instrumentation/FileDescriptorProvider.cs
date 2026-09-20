using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Instrumentation;

/// <summary>
/// Provides file descriptor and handle metrics across Linux, macOS, and Windows.
/// </summary>
public class FileDescriptorProvider : IFileDescriptorProvider
{
    private readonly Logger _logger;
    private readonly string _procFdPath;
    private readonly string _procLimitsPath;
    private readonly Func<int> _handleCountAccessor;
    private readonly Func<bool> _isWindowsAccessor;

    public FileDescriptorProvider()
        : this("/proc/self/fd", "/proc/self/limits", null, null)
    {
    }

    public FileDescriptorProvider(
        string procFdPath,
        string procLimitsPath,
        Func<int> handleCountAccessor = null,
        Func<bool> isWindowsAccessor = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _procFdPath = procFdPath ?? "/proc/self/fd";
        _procLimitsPath = procLimitsPath ?? "/proc/self/limits";
        _handleCountAccessor = handleCountAccessor ?? (() => Process.GetCurrentProcess().HandleCount);
        _isWindowsAccessor = isWindowsAccessor ?? (() => OsInfo.IsWindows);
    }

    public int GetOpenFileDescriptorCount()
    {
        if (!_isWindowsAccessor() && Directory.Exists(_procFdPath))
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(_procFdPath).Count();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to count file descriptors from {0}", _procFdPath);
            }
        }

        try
        {
            return _handleCountAccessor();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to retrieve handle count from process");
            return -1;
        }
    }

    public int GetMaxFileDescriptors()
    {
        if (!_isWindowsAccessor() && File.Exists(_procLimitsPath))
        {
            try
            {
                var lines = File.ReadLines(_procLimitsPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Max open files", StringComparison.OrdinalIgnoreCase))
                    {
                        var remainder = line.Substring("Max open files".Length).Trim();
                        var parts = remainder.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                        if (parts.Length > 0)
                        {
                            if (string.Equals(parts[0], "unlimited", StringComparison.OrdinalIgnoreCase))
                            {
                                return -1;
                            }

                            if (int.TryParse(parts[0], out var softLimit))
                            {
                                return softLimit;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse max open files limit from {0}", _procLimitsPath);
            }
        }

        return -1;
    }

    public double? GetFileDescriptorUsagePercentage()
    {
        var count = GetOpenFileDescriptorCount();
        var max = GetMaxFileDescriptors();

        if (count < 0 || max <= 0)
        {
            return null;
        }

        var percentage = ((double)count / max) * 100.0;
        return Math.Round(percentage, 2);
    }
}
