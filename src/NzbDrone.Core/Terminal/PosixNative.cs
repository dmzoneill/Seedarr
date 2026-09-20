#pragma warning disable SA1307
#pragma warning disable SA1310
#pragma warning disable SA1300
#pragma warning disable SA1117
#pragma warning disable IDE0007

using System;
using System.Runtime.InteropServices;

namespace NzbDrone.Core.Terminal;

[StructLayout(LayoutKind.Sequential)]
public struct Winsize
{
    public ushort ws_row;
    public ushort ws_col;
    public ushort ws_xpixel;
    public ushort ws_ypixel;
}

[StructLayout(LayoutKind.Sequential)]
public struct PollFd
{
    public int fd;
    public short events;
    public short revents;
}

public static class PosixNative
{
    public const short POLLIN = 0x0001;
    public const short POLLPRI = 0x0002;
    public const short POLLOUT = 0x0004;
    public const short POLLERR = 0x0008;
    public const short POLLHUP = 0x0010;
    public const short POLLNVAL = 0x0020;

    public const ulong TIOCSWINSZ_LINUX = 0x5414;
    public const ulong TIOCSWINSZ_OSX = 0x80087467;

    public const int SIGHUP = 1;
    public const int SIGINT = 2;
    public const int SIGKILL = 9;
    public const int SIGTERM = 15;

    public const int WNOHANG = 1;

    public const int EINTR = 4;
    public const int EIO = 5;
    public const int EPIPE = 32;

    public const short POSIX_SPAWN_SETPGROUP = 0x0002;

    [DllImport("libc", EntryPoint = "openpty", SetLastError = true)]
    private static extern int openpty_libc(out int amaster, out int aslave, IntPtr name, IntPtr termp, ref Winsize winp);

    [DllImport("libutil", EntryPoint = "openpty", SetLastError = true)]
    private static extern int openpty_libutil(out int amaster, out int aslave, IntPtr name, IntPtr termp, ref Winsize winp);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int ioctl(int fd, ulong request, ref Winsize winp);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    public static extern nint read(int fd, [Out] byte[] buf, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    public static extern nint write(int fd, [In] byte[] buf, nuint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    public static extern int kill(int pid, int sig);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    public static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    public static extern int poll(ref PollFd fds, uint nfds, int timeout);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init", SetLastError = true)]
    public static extern int posix_spawn_file_actions_init(IntPtr file_actions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy", SetLastError = true)]
    public static extern int posix_spawn_file_actions_destroy(IntPtr file_actions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2", SetLastError = true)]
    public static extern int posix_spawn_file_actions_adddup2(IntPtr file_actions, int fd, int newfd);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose", SetLastError = true)]
    public static extern int posix_spawn_file_actions_addclose(IntPtr file_actions, int fd);

    [DllImport("libc", EntryPoint = "posix_spawnattr_init", SetLastError = true)]
    public static extern int posix_spawnattr_init(IntPtr attr);

    [DllImport("libc", EntryPoint = "posix_spawnattr_destroy", SetLastError = true)]
    public static extern int posix_spawnattr_destroy(IntPtr attr);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setflags", SetLastError = true)]
    public static extern int posix_spawnattr_setflags(IntPtr attr, short flags);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setpgroup", SetLastError = true)]
    public static extern int posix_spawnattr_setpgroup(IntPtr attr, int pgroup);

    [DllImport("libc", EntryPoint = "posix_spawn", SetLastError = true)]
    public static extern int posix_spawn(
        out int pid,
        IntPtr path,
        IntPtr file_actions,
        IntPtr attrp,
        IntPtr argv,
        IntPtr envp);

    public static int OpenPty(out int masterFd, out int slaveFd, ref Winsize win)
    {
        try
        {
            return openpty_libc(out masterFd, out slaveFd, IntPtr.Zero, IntPtr.Zero, ref win);
        }
        catch (EntryPointNotFoundException)
        {
            return openpty_libutil(out masterFd, out slaveFd, IntPtr.Zero, IntPtr.Zero, ref win);
        }
        catch (DllNotFoundException)
        {
            return openpty_libutil(out masterFd, out slaveFd, IntPtr.Zero, IntPtr.Zero, ref win);
        }
    }
}
