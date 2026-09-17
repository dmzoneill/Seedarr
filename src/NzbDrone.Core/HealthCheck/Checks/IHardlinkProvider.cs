using System;
using System.Runtime.InteropServices;

namespace NzbDrone.Core.HealthCheck.Checks;

public interface IHardlinkProvider
{
    bool TryCreateHardLink(string sourcePath, string destinationPath, out string error);
}

public class HardlinkProvider : IHardlinkProvider
{
    public bool TryCreateHardLink(string sourcePath, string destinationPath, out string error)
    {
        error = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!CreateHardLinkW(destinationPath, sourcePath, IntPtr.Zero))
                {
                    var winErr = Marshal.GetLastWin32Error();
                    error = $"Win32 error {winErr}";
                    return false;
                }

                return true;
            }

            var result = link(sourcePath, destinationPath);
            if (result != 0)
            {
                var errno = Marshal.GetLastWin32Error();
                error = errno switch
                {
                    18 => "EXDEV (Invalid cross-device link)",
                    1 => "EPERM (Operation not permitted)",
                    95 => "EOPNOTSUPP (Operation not supported)",
                    _ => $"errno {errno}",
                };
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        [MarshalAs(UnmanagedType.LPWStr)] string existingFileName,
        IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int link(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);
}
