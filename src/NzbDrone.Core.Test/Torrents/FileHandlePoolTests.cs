using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class FileHandlePoolTests
{
    private string _tempDir;
    private FileHandlePool _pool;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileHandlePoolTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _pool = new FileHandlePool(maxCapacity: 3);
    }

    [TearDown]
    public void TearDown()
    {
        _pool?.Dispose();

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Test]
    public void Constructor_sets_max_capacity_and_initial_zero_count()
    {
        using var defaultPool = new FileHandlePool();
        Assert.That(defaultPool.MaxCapacity, Is.EqualTo(256));
        Assert.That(defaultPool.Count, Is.EqualTo(0));

        using var customPool = new FileHandlePool(64);
        Assert.That(customPool.MaxCapacity, Is.EqualTo(64));
        Assert.That(customPool.Count, Is.EqualTo(0));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-50)]
    public void Constructor_throws_when_max_capacity_is_zero_or_negative(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileHandlePool(capacity));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetOrCreateHandle_throws_when_path_is_null_or_whitespace(string path)
    {
        Assert.Throws<ArgumentException>(() => _pool.GetOrCreateHandle(path));
    }

    [Test]
    public void GetOrCreateHandle_creates_file_and_nested_parent_directories_with_write_access()
    {
        var nestedPath = Path.Combine(_tempDir, "subdir1", "subdir2", "test_file.dat");

        Assert.That(File.Exists(nestedPath), Is.False);

        var handle = _pool.GetOrCreateHandle(nestedPath, writeAccess: true);

        Assert.That(handle, Is.Not.Null);
        Assert.That(handle.IsInvalid, Is.False);
        Assert.That(handle.IsClosed, Is.False);
        Assert.That(File.Exists(nestedPath), Is.True);
        Assert.That(_pool.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetOrCreateHandle_returns_same_cached_handle_for_identical_file()
    {
        var filePath = Path.Combine(_tempDir, "cached_file.dat");

        var handle1 = _pool.GetOrCreateHandle(filePath, writeAccess: true);
        var handle2 = _pool.GetOrCreateHandle(filePath, writeAccess: true);

        Assert.That(handle2, Is.SameAs(handle1));
        Assert.That(_pool.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetOrCreateHandle_normalizes_paths_and_matches_relative_to_full_path()
    {
        var fileName = "relative_test.dat";
        var fullPath = Path.Combine(_tempDir, fileName);

        var handle1 = _pool.GetOrCreateHandle(fullPath, writeAccess: true);

        var currentDir = Directory.GetCurrentDirectory();
        var relativePath = Path.GetRelativePath(currentDir, fullPath);

        var handle2 = _pool.GetOrCreateHandle(relativePath, writeAccess: true);

        Assert.That(handle2, Is.SameAs(handle1));
        Assert.That(_pool.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetOrCreateHandle_opens_existing_file_for_read_access()
    {
        var filePath = Path.Combine(_tempDir, "existing_read.dat");
        File.WriteAllText(filePath, "Seedarr test payload");

        var handle = _pool.GetOrCreateHandle(filePath, writeAccess: false);

        Assert.That(handle, Is.Not.Null);
        Assert.That(handle.IsInvalid, Is.False);
        Assert.That(handle.IsClosed, Is.False);
        Assert.That(_pool.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetOrCreateHandle_reopens_handle_when_upgrading_from_read_only_to_write_access()
    {
        var filePath = Path.Combine(_tempDir, "upgrade_file.dat");
        File.WriteAllText(filePath, "Read-only seed");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        try
        {
            var readHandle = _pool.GetOrCreateHandle(filePath, writeAccess: false);
            Assert.That(readHandle.IsClosed, Is.False);

            File.SetAttributes(filePath, FileAttributes.Normal);

            var writeHandle = _pool.GetOrCreateHandle(filePath, writeAccess: true);

            Assert.That(writeHandle, Is.Not.SameAs(readHandle));
            Assert.That(readHandle.IsClosed, Is.True);
            Assert.That(writeHandle.IsClosed, Is.False);
            Assert.That(_pool.Count, Is.EqualTo(1));
        }
        finally
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void CloseHandle_returns_false_for_null_or_whitespace(string path)
    {
        Assert.That(_pool.CloseHandle(path), Is.False);
    }

    [Test]
    public void CloseHandle_returns_false_for_untracked_file()
    {
        var nonExistent = Path.Combine(_tempDir, "untracked.dat");
        Assert.That(_pool.CloseHandle(nonExistent), Is.False);
    }

    [Test]
    public void CloseHandle_removes_handle_from_pool_and_disposes_it()
    {
        var filePath = Path.Combine(_tempDir, "to_close.dat");
        var handle = _pool.GetOrCreateHandle(filePath, writeAccess: true);

        Assert.That(_pool.Contains(filePath), Is.True);
        Assert.That(_pool.Count, Is.EqualTo(1));

        var closed = _pool.CloseHandle(filePath);

        Assert.That(closed, Is.True);
        Assert.That(handle.IsClosed, Is.True);
        Assert.That(_pool.Contains(filePath), Is.False);
        Assert.That(_pool.Count, Is.EqualTo(0));

        var closedAgain = _pool.CloseHandle(filePath);
        Assert.That(closedAgain, Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Contains_returns_false_for_null_or_whitespace(string path)
    {
        Assert.That(_pool.Contains(path), Is.False);
    }

    [Test]
    public void Contains_returns_true_for_active_file_and_false_for_untracked()
    {
        var filePath1 = Path.Combine(_tempDir, "file1.dat");
        var filePath2 = Path.Combine(_tempDir, "file2.dat");

        _pool.GetOrCreateHandle(filePath1, writeAccess: true);

        Assert.That(_pool.Contains(filePath1), Is.True);
        Assert.That(_pool.Contains(filePath2), Is.False);
    }

    [Test]
    public void Contains_removes_and_returns_false_for_externally_closed_handle()
    {
        var filePath = Path.Combine(_tempDir, "externally_closed.dat");
        var handle = _pool.GetOrCreateHandle(filePath, writeAccess: true);

        Assert.That(_pool.Contains(filePath), Is.True);

        handle.Dispose();

        Assert.That(_pool.Contains(filePath), Is.False);
        Assert.That(_pool.Count, Is.EqualTo(0));
    }

    [Test]
    public void GetOrCreateHandle_replaces_externally_closed_handle_with_fresh_handle()
    {
        var filePath = Path.Combine(_tempDir, "reopen_closed.dat");
        var initialHandle = _pool.GetOrCreateHandle(filePath, writeAccess: true);

        initialHandle.Dispose();

        var freshHandle = _pool.GetOrCreateHandle(filePath, writeAccess: true);

        Assert.That(freshHandle, Is.Not.SameAs(initialHandle));
        Assert.That(freshHandle.IsClosed, Is.False);
        Assert.That(freshHandle.IsInvalid, Is.False);
        Assert.That(_pool.Count, Is.EqualTo(1));
    }

    [Test]
    public void Lru_eviction_removes_least_recently_used_handle_when_pool_capacity_exceeded()
    {
        var fileA = Path.Combine(_tempDir, "fileA.dat");
        var fileB = Path.Combine(_tempDir, "fileB.dat");
        var fileC = Path.Combine(_tempDir, "fileC.dat");
        var fileD = Path.Combine(_tempDir, "fileD.dat");

        var handleA = _pool.GetOrCreateHandle(fileA, writeAccess: true);
        var handleB = _pool.GetOrCreateHandle(fileB, writeAccess: true);
        var handleC = _pool.GetOrCreateHandle(fileC, writeAccess: true);

        Assert.That(_pool.Count, Is.EqualTo(3));
        Assert.That(handleA.IsClosed, Is.False);

        // Capacity is 3. Opening file D should evict least recently used (file A)
        var handleD = _pool.GetOrCreateHandle(fileD, writeAccess: true);

        Assert.That(_pool.Count, Is.EqualTo(3));
        Assert.That(_pool.Contains(fileA), Is.False);
        Assert.That(handleA.IsClosed, Is.True);
        Assert.That(_pool.Contains(fileB), Is.True);
        Assert.That(_pool.Contains(fileC), Is.True);
        Assert.That(_pool.Contains(fileD), Is.True);
    }

    [Test]
    public void Lru_eviction_updates_access_order_when_handle_is_reused()
    {
        var fileA = Path.Combine(_tempDir, "orderA.dat");
        var fileB = Path.Combine(_tempDir, "orderB.dat");
        var fileC = Path.Combine(_tempDir, "orderC.dat");
        var fileD = Path.Combine(_tempDir, "orderD.dat");

        var handleA = _pool.GetOrCreateHandle(fileA, writeAccess: true);
        var handleB = _pool.GetOrCreateHandle(fileB, writeAccess: true);
        var handleC = _pool.GetOrCreateHandle(fileC, writeAccess: true);

        // Access file A again to make it most recently used (LRU order becomes: A (MRU), C, B (LRU))
        _pool.GetOrCreateHandle(fileA, writeAccess: true);

        // Adding file D should now evict file B instead of file A
        var handleD = _pool.GetOrCreateHandle(fileD, writeAccess: true);

        Assert.That(_pool.Count, Is.EqualTo(3));
        Assert.That(_pool.Contains(fileB), Is.False);
        Assert.That(handleB.IsClosed, Is.True);
        Assert.That(_pool.Contains(fileA), Is.True);
        Assert.That(_pool.Contains(fileC), Is.True);
        Assert.That(_pool.Contains(fileD), Is.True);
    }

    [Test]
    public void Clear_disposes_all_handles_and_empties_pool()
    {
        var file1 = Path.Combine(_tempDir, "clear1.dat");
        var file2 = Path.Combine(_tempDir, "clear2.dat");

        var handle1 = _pool.GetOrCreateHandle(file1, writeAccess: true);
        var handle2 = _pool.GetOrCreateHandle(file2, writeAccess: true);

        Assert.That(_pool.Count, Is.EqualTo(2));

        _pool.Clear();

        Assert.That(_pool.Count, Is.EqualTo(0));
        Assert.That(_pool.Contains(file1), Is.False);
        Assert.That(_pool.Contains(file2), Is.False);
        Assert.That(handle1.IsClosed, Is.True);
        Assert.That(handle2.IsClosed, Is.True);
    }

    [Test]
    public void Dispose_disposes_all_handles_and_disallows_subsequent_operations()
    {
        var file1 = Path.Combine(_tempDir, "dispose1.dat");
        var handle1 = _pool.GetOrCreateHandle(file1, writeAccess: true);

        _pool.Dispose();

        Assert.That(handle1.IsClosed, Is.True);
        Assert.That(_pool.Contains(file1), Is.False);
        Assert.That(_pool.CloseHandle(file1), Is.False);

        Assert.Throws<ObjectDisposedException>(() =>
            _pool.GetOrCreateHandle(Path.Combine(_tempDir, "after_dispose.dat"), writeAccess: true));

        // Subsequent Dispose and Clear should be safe and idempotent
        Assert.DoesNotThrow(() => _pool.Dispose());
        Assert.DoesNotThrow(() => _pool.Clear());
    }

    [Test]
    public void Concurrent_access_preserves_capacity_limit_and_handles_threads_safely()
    {
        using var smallPool = new FileHandlePool(maxCapacity: 5);
        var files = new string[15];
        for (var i = 0; i < files.Length; i++)
        {
            files[i] = Path.Combine(_tempDir, $"concurrent_{i}.dat");
        }

        Parallel.For(0, 100, i =>
        {
            var targetFile = files[i % files.Length];

            if (i % 3 == 0)
            {
                smallPool.CloseHandle(targetFile);
            }
            else
            {
                var handle = smallPool.GetOrCreateHandle(targetFile, writeAccess: true);
                Assert.That(handle, Is.Not.Null);
            }

            Assert.That(smallPool.Count, Is.LessThanOrEqualTo(smallPool.MaxCapacity));
        });

        Assert.That(smallPool.Count, Is.LessThanOrEqualTo(5));
    }
}
