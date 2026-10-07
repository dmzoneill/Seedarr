using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NzbDrone.Common.Disk;

internal static class WindowsVolumeHelper
{
    private const int ErrorFilenameExceedsRange = 206;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false)]
    private static extern bool GetVolumePathName(string lpszFileName, StringBuilder lpszVolumePathName, int cchBufferLength);

    public static string GetVolumePathForFile(string filePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var buffer = new StringBuilder(261);
        while (!GetVolumePathName(filePath, buffer, buffer.Capacity))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorFilenameExceedsRange)
            {
                return null;
            }

            buffer.EnsureCapacity(buffer.Capacity * 2);
        }

        return buffer.Length == 0 ? null : buffer.ToString();
    }
}
