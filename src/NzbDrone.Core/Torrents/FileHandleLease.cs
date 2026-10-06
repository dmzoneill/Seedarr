using System;
using Microsoft.Win32.SafeHandles;

namespace NzbDrone.Core.Torrents;

public readonly struct FileHandleLease : IDisposable
{
    private readonly FileHandlePool _pool;
    private readonly string _path;
    private readonly SafeFileHandle _handle;

    public SafeFileHandle Handle => _handle;

    public FileHandleLease(FileHandlePool pool, string path, SafeFileHandle handle)
    {
        _pool = pool;
        _path = path;
        _handle = handle;
    }

    public void Dispose()
    {
        _pool?.ReleaseHandle(_path, _handle);
    }
}
