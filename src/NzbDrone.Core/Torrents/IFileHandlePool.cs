using System;
using Microsoft.Win32.SafeHandles;

namespace NzbDrone.Core.Torrents;

public interface IFileHandlePool : IDisposable
{
    SafeFileHandle GetOrCreateHandle(string filePath, bool writeAccess = false);

    bool CloseHandle(string filePath);

    bool Contains(string filePath);

    void Clear();

    int Count { get; }

    int MaxCapacity { get; }
}
