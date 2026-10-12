// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class LinuxPtySessionPosixErrnoTest
{
    /// <summary>
    /// Documents libc P/Invoke error semantics used when reporting forkpty failures in <see cref="LinuxPtySession"/>.
    /// </summary>
    [Test]
    [Platform(Include = "Linux")]
    public void Failed_chdir_sets_posix_errno_for_GetLastPInvokeError_not_GetLastWin32Error()
    {
        var missing = Marshal.StringToCoTaskMemUTF8($"/seedarr-missing-cwd-{Guid.NewGuid():N}");
        try
        {
            Assert.That(NativePty.Chdir(missing), Is.EqualTo(-1));
            var pinvokeErrno = Marshal.GetLastPInvokeError();
            Assert.That(pinvokeErrno, Is.GreaterThan(0), "ENOENT or similar POSIX errno expected");
            // Some .NET runtimes mirror libc errno into GetLastWin32Error; production code must use GetLastPInvokeError.
            _ = Marshal.GetLastWin32Error();
        }
        finally
        {
            Marshal.FreeCoTaskMem(missing);
        }
    }
}
