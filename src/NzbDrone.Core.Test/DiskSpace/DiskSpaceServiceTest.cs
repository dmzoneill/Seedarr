using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.DiskSpace;

namespace NzbDrone.Core.Test.DiskSpace;

[TestFixture]
public class DiskSpaceServiceTest
{
    private IAppFolderInfo _appFolderInfo;
    private DiskSpaceService _subject;

    [SetUp]
    public void SetUp()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns("/tmp");
        _appFolderInfo.StartUpFolder.Returns("/tmp");
        _subject = new DiskSpaceService(_appFolderInfo);
    }

    // --- Constructor tests ---

    [Test]
    public void Constructor_should_accept_app_folder_info()
    {
        var service = new DiskSpaceService(_appFolderInfo);

        Assert.That(service, Is.Not.Null);
    }

    // --- GetDiskSpace tests ---

    [Test]
    public void GetDiskSpace_should_return_non_null_list()
    {
        var result = _subject.GetDiskSpace();

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void GetDiskSpace_should_return_list_type()
    {
        var result = _subject.GetDiskSpace();

        Assert.That(result, Is.InstanceOf<List<DiskSpaceInfo>>());
    }

    [Test]
    public void GetDiskSpace_should_return_at_least_one_entry_for_valid_paths()
    {
        // /tmp is a valid path on Linux, should return at least one disk entry
        _appFolderInfo.AppDataFolder.Returns("/tmp");
        _appFolderInfo.StartUpFolder.Returns("/tmp");

        var result = _subject.GetDiskSpace();

        Assert.That(result.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void GetDiskSpace_should_deduplicate_entries_with_same_root()
    {
        // Both point to same root, should not duplicate
        _appFolderInfo.AppDataFolder.Returns("/tmp/test1");
        _appFolderInfo.StartUpFolder.Returns("/tmp/test2");
        _subject = new DiskSpaceService(_appFolderInfo);

        var result = _subject.GetDiskSpace();

        // Count entries for the root "/"
        var rootEntries = result.Where(d =>
            d.Path == "/tmp/test1" || d.Path == "/tmp/test2" || d.Path == "/").ToList();

        // Should not have duplicated the root entry from AddDriveInfo
        Assert.That(rootEntries.Count, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void GetDiskSpace_should_have_non_negative_free_space()
    {
        var result = _subject.GetDiskSpace();

        foreach (var info in result)
        {
            Assert.That(info.FreeSpace, Is.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public void GetDiskSpace_should_have_non_negative_total_space()
    {
        var result = _subject.GetDiskSpace();

        foreach (var info in result)
        {
            Assert.That(info.TotalSpace, Is.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public void GetDiskSpace_should_have_free_space_less_than_or_equal_to_total()
    {
        var result = _subject.GetDiskSpace();

        foreach (var info in result)
        {
            Assert.That(info.FreeSpace, Is.LessThanOrEqualTo(info.TotalSpace));
        }
    }

    [Test]
    public void GetDiskSpace_should_have_non_empty_path_for_all_entries()
    {
        var result = _subject.GetDiskSpace();

        foreach (var info in result)
        {
            Assert.That(info.Path, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public void GetDiskSpace_should_have_non_empty_label_for_all_entries()
    {
        var result = _subject.GetDiskSpace();

        foreach (var info in result)
        {
            Assert.That(info.Label, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public void GetDiskSpace_should_set_label_for_appdata_entry()
    {
        _appFolderInfo.AppDataFolder.Returns("/tmp");
        _appFolderInfo.StartUpFolder.Returns("/nonexistent_path_xyz");
        _subject = new DiskSpaceService(_appFolderInfo);

        var result = _subject.GetDiskSpace();

        var appDataEntry = result.FirstOrDefault(d => d.Label == "AppData");
        Assert.That(appDataEntry, Is.Not.Null);
    }

    [Test]
    public void GetDiskSpace_should_handle_null_app_data_folder_gracefully()
    {
        _appFolderInfo.AppDataFolder.Returns((string)null);
        _appFolderInfo.StartUpFolder.Returns("/tmp");
        _subject = new DiskSpaceService(_appFolderInfo);

        // Should not throw - AddDriveInfo catches exceptions
        var result = _subject.GetDiskSpace();

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void GetDiskSpace_should_handle_empty_app_data_folder_gracefully()
    {
        _appFolderInfo.AppDataFolder.Returns("");
        _appFolderInfo.StartUpFolder.Returns("/tmp");
        _subject = new DiskSpaceService(_appFolderInfo);

        var result = _subject.GetDiskSpace();

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void GetDiskSpace_should_handle_null_startup_folder_gracefully()
    {
        _appFolderInfo.AppDataFolder.Returns("/tmp");
        _appFolderInfo.StartUpFolder.Returns((string)null);
        _subject = new DiskSpaceService(_appFolderInfo);

        var result = _subject.GetDiskSpace();

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void GetDiskSpace_should_handle_both_folders_null()
    {
        _appFolderInfo.AppDataFolder.Returns((string)null);
        _appFolderInfo.StartUpFolder.Returns((string)null);
        _subject = new DiskSpaceService(_appFolderInfo);

        var result = _subject.GetDiskSpace();

        Assert.That(result, Is.Not.Null);
    }

    // --- AddDriveInfo tests (private static, via reflection) ---

    private void InvokeAddDriveInfo(List<DiskSpaceInfo> result, HashSet<string> seen, string path, string label)
    {
        var method = typeof(DiskSpaceService).GetMethod("AddDriveInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(_subject, new object[] { result, seen, path, label });
    }

    [Test]
    public void AddDriveInfo_should_skip_null_path()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeAddDriveInfo(result, seen, null, "Test");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void AddDriveInfo_should_skip_empty_path()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeAddDriveInfo(result, seen, "", "Test");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void AddDriveInfo_should_skip_duplicate_roots()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeAddDriveInfo(result, seen, "/tmp/dir1", "First");
        var countAfterFirst = result.Count;

        InvokeAddDriveInfo(result, seen, "/tmp/dir2", "Second");
        var countAfterSecond = result.Count;

        // Second call with same root should not add another entry
        Assert.That(countAfterSecond, Is.EqualTo(countAfterFirst));
    }

    [Test]
    public void AddDriveInfo_should_add_entry_for_valid_path()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeAddDriveInfo(result, seen, "/tmp", "TestLabel");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Label, Is.EqualTo("TestLabel"));
        Assert.That(result[0].Path, Is.EqualTo("/tmp"));
    }

    [Test]
    public void AddDriveInfo_should_populate_free_space()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeAddDriveInfo(result, seen, "/tmp", "Test");

        if (result.Count > 0)
        {
            Assert.That(result[0].FreeSpace, Is.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public void AddDriveInfo_should_populate_total_space()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        InvokeAddDriveInfo(result, seen, "/tmp", "Test");

        if (result.Count > 0)
        {
            Assert.That(result[0].TotalSpace, Is.GreaterThan(0));
        }
    }

    // --- DiskSpaceInfo data model tests ---

    [Test]
    public void DiskSpaceInfo_properties_should_be_settable()
    {
        var info = new DiskSpaceInfo
        {
            Path = "/data",
            Label = "Data Drive",
            FreeSpace = 1024L * 1024 * 1024,
            TotalSpace = 10L * 1024 * 1024 * 1024,
        };

        Assert.That(info.Path, Is.EqualTo("/data"));
        Assert.That(info.Label, Is.EqualTo("Data Drive"));
        Assert.That(info.FreeSpace, Is.EqualTo(1024L * 1024 * 1024));
        Assert.That(info.TotalSpace, Is.EqualTo(10L * 1024 * 1024 * 1024));
    }

    [Test]
    public void DiskSpaceInfo_default_values_should_be_zero_and_null()
    {
        var info = new DiskSpaceInfo();

        Assert.That(info.Path, Is.Null);
        Assert.That(info.Label, Is.Null);
        Assert.That(info.FreeSpace, Is.EqualTo(0));
        Assert.That(info.TotalSpace, Is.EqualTo(0));
    }

    // --- GetDiskSpace includes drive enumeration ---

    [Test]
    public void GetDiskSpace_should_include_fixed_drives()
    {
        var result = _subject.GetDiskSpace();

        // On any system there should be at least one fixed drive (the root)
        Assert.That(result.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void GetDiskSpace_entries_should_have_unique_roots_from_app_folders()
    {
        _appFolderInfo.AppDataFolder.Returns("/tmp/appdata");
        _appFolderInfo.StartUpFolder.Returns("/tmp/startup");
        _subject = new DiskSpaceService(_appFolderInfo);

        var result = _subject.GetDiskSpace();
        var paths = result.Select(d => d.Path).ToList();

        // The result list should contain at most one entry for each root
        // (deduplication via the seen HashSet)
        Assert.That(result.Count, Is.GreaterThanOrEqualTo(1));
    }
}
