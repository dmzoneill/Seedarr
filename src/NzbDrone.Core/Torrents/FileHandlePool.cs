using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace NzbDrone.Core.Torrents;

public class FileHandlePool : IFileHandlePool
{
    private const int DefaultMaxCapacity = 256;
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly int _maxCapacity;
    private readonly object _lock = new();
    private readonly Dictionary<string, LinkedListNode<HandleEntry>> _cache;
    private readonly LinkedList<HandleEntry> _lruList;
    private bool _disposed;

    public int MaxCapacity => _maxCapacity;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }

    public FileHandlePool(int maxCapacity = DefaultMaxCapacity)
    {
        if (maxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCapacity), "Max capacity must be greater than zero.");
        }

        _maxCapacity = maxCapacity;
        _cache = new Dictionary<string, LinkedListNode<HandleEntry>>(PathComparer);
        _lruList = new LinkedList<HandleEntry>();
    }

    public SafeFileHandle GetOrCreateHandle(string filePath, bool writeAccess = false)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));
        }

        var normalizedPath = Path.GetFullPath(filePath);

        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FileHandlePool));
            }

            if (_cache.TryGetValue(normalizedPath, out var node))
            {
                if (node.Value.Handle.IsClosed || node.Value.Handle.IsInvalid)
                {
                    _cache.Remove(normalizedPath);
                    _lruList.Remove(node);
                    node.Value.Handle.Dispose();
                }
                else if (!writeAccess || node.Value.CanWrite)
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    return node.Value.Handle;
                }
                else
                {
                    _cache.Remove(normalizedPath);
                    _lruList.Remove(node);
                    node.Value.Handle.Dispose();
                }
            }

            while (_cache.Count >= _maxCapacity && _lruList.Last != null)
            {
                var lru = _lruList.Last;
                _lruList.RemoveLast();
                _cache.Remove(lru.Value.Path);
                try
                {
                    lru.Value.Handle.Dispose();
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            SafeFileHandle handle;
            bool canWrite;

            if (writeAccess)
            {
                var directory = Path.GetDirectoryName(normalizedPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                handle = File.OpenHandle(
                    normalizedPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);

                canWrite = true;
            }
            else
            {
                try
                {
                    handle = File.OpenHandle(
                        normalizedPath,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite);

                    canWrite = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    handle = File.OpenHandle(
                        normalizedPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);

                    canWrite = false;
                }
            }

            var entry = new HandleEntry(normalizedPath, handle, canWrite);
            var newNode = _lruList.AddFirst(entry);
            _cache[normalizedPath] = newNode;

            return handle;
        }
    }

    public bool CloseHandle(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(filePath);

        lock (_lock)
        {
            if (_disposed)
            {
                return false;
            }

            if (_cache.TryGetValue(normalizedPath, out var node))
            {
                _cache.Remove(normalizedPath);
                _lruList.Remove(node);
                try
                {
                    node.Value.Handle.Dispose();
                }
                catch
                {
                    // Best effort cleanup
                }

                return true;
            }

            return false;
        }
    }

    public bool Contains(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedPath = Path.GetFullPath(filePath);

        lock (_lock)
        {
            if (_disposed)
            {
                return false;
            }

            if (_cache.TryGetValue(normalizedPath, out var node))
            {
                if (!node.Value.Handle.IsClosed && !node.Value.Handle.IsInvalid)
                {
                    return true;
                }

                _cache.Remove(normalizedPath);
                _lruList.Remove(node);
                try
                {
                    node.Value.Handle.Dispose();
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            return false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var entry in _lruList)
            {
                try
                {
                    entry.Handle.Dispose();
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            _cache.Clear();
            _lruList.Clear();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var entry in _lruList)
            {
                try
                {
                    entry.Handle.Dispose();
                }
                catch
                {
                    // Best effort cleanup
                }
            }

            _cache.Clear();
            _lruList.Clear();
        }
    }

    private sealed class HandleEntry
    {
        public string Path { get; }
        public SafeFileHandle Handle { get; }
        public bool CanWrite { get; }

        public HandleEntry(string path, SafeFileHandle handle, bool canWrite)
        {
            Path = path;
            Handle = handle;
            CanWrite = canWrite;
        }
    }
}
