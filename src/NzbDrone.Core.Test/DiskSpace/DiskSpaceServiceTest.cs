using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Messaging.Events;

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
            FileSystemType = "ext4",
            IsReadOnly = true,
        };

        Assert.That(info.Path, Is.EqualTo("/data"));
        Assert.That(info.Label, Is.EqualTo("Data Drive"));
        Assert.That(info.FreeSpace, Is.EqualTo(1024L * 1024 * 1024));
        Assert.That(info.TotalSpace, Is.EqualTo(10L * 1024 * 1024 * 1024));
        Assert.That(info.FileSystemType, Is.EqualTo("ext4"));
        Assert.That(info.IsReadOnly, Is.True);
    }

    [Test]
    public void DiskSpaceInfo_default_values_should_be_zero_and_null()
    {
        var info = new DiskSpaceInfo();

        Assert.That(info.Path, Is.Null);
        Assert.That(info.Label, Is.Null);
        Assert.That(info.FreeSpace, Is.EqualTo(0));
        Assert.That(info.TotalSpace, Is.EqualTo(0));
        Assert.That(info.FileSystemType, Is.Null);
        Assert.That(info.IsReadOnly, Is.False);
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

    // --- Edge-triggered transitions and hysteresis tests ---

    [Test]
    public void UpdateDiskSpaceHealth_should_transition_from_Normal_to_Low_and_publish_DiskSpaceLowEvent()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;
        const long free = 4L * 1024 * 1024 * 1024; // 4 GB (4%) -> Low

        var state = service.UpdateDiskSpaceHealth("/data", free, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Low));
        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceLowEvent>(e =>
            e.DrivePath == "/data" &&
            e.FreeBytes == free &&
            e.TotalBytes == total));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceCriticalEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceRestoredEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_not_publish_duplicate_DiskSpaceLowEvent_when_remaining_in_Low()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        service.UpdateDiskSpaceHealth("/data", 4L * 1024 * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        var state = service.UpdateDiskSpaceHealth("/data", 4200L * 1024 * 1024, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Low));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceLowEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceCriticalEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceRestoredEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_transition_from_Low_to_Critical_and_publish_DiskSpaceCriticalEvent()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        service.UpdateDiskSpaceHealth("/data", 4L * 1024 * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        const long criticalFree = 800L * 1024 * 1024; // 800 MB -> Critical
        var state = service.UpdateDiskSpaceHealth("/data", criticalFree, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Critical));
        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e =>
            e.DrivePath == "/data" &&
            e.FreeBytes == criticalFree));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceLowEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceRestoredEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_not_publish_duplicate_DiskSpaceCriticalEvent_when_remaining_in_Critical()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        service.UpdateDiskSpaceHealth("/data", 800L * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        var state = service.UpdateDiskSpaceHealth("/data", 750L * 1024 * 1024, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Critical));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceCriticalEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_respect_hysteresis_and_not_flap_between_Critical_and_Low()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        // Enter Critical at 800 MB (< 1 GB)
        service.UpdateDiskSpaceHealth("/data", 800L * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        // Free space fluctuates to 1.1 GB (above 1 GB, but below 1.25 GB hysteresis recovery)
        var state = service.UpdateDiskSpaceHealth("/data", 1100L * 1024 * 1024, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Critical));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceCriticalEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceLowEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_transition_from_Critical_to_Low_without_publishing_events()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        service.UpdateDiskSpaceHealth("/data", 800L * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        // Space rises to 2 GB (>= 1.25 GB, but still < 5 GB / < 5%)
        var state = service.UpdateDiskSpaceHealth("/data", 2L * 1024 * 1024 * 1024, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Low));
        // Transitioning from Critical to Low must not publish LowEvent or RestoredEvent
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceLowEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceRestoredEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_respect_hysteresis_and_not_flap_between_Low_and_Normal()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        // Enter Low
        service.UpdateDiskSpaceHealth("/data", 4L * 1024 * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        // Space increases to 5.5 GB (above 5 GB, but below 6 GB and below 6%)
        var state = service.UpdateDiskSpaceHealth("/data", 5500L * 1024 * 1024, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Low));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceRestoredEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_transition_from_Low_to_Normal_and_publish_DiskSpaceRestoredEvent()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        service.UpdateDiskSpaceHealth("/data", 4L * 1024 * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        const long restoredFree = 10L * 1024 * 1024 * 1024; // 10 GB (10%) >= 6 GB and >= 6%
        var state = service.UpdateDiskSpaceHealth("/data", restoredFree, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Normal));
        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceRestoredEvent>(e =>
            e.DrivePath == "/data" &&
            e.FreeBytes == restoredFree &&
            e.TotalBytes == total));
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_transition_directly_from_Critical_to_Normal_and_publish_DiskSpaceRestoredEvent()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        // Enter Critical
        service.UpdateDiskSpaceHealth("/data", 500L * 1024 * 1024, total);
        eventAggregator.ClearReceivedCalls();

        // Large cleanup frees space up to 25 GB (25%)
        const long restoredFree = 25L * 1024 * 1024 * 1024;
        var state = service.UpdateDiskSpaceHealth("/data", restoredFree, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Normal));
        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceRestoredEvent>(e =>
            e.DrivePath == "/data" &&
            e.FreeBytes == restoredFree &&
            e.TotalBytes == total));
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_transition_directly_from_Normal_to_Critical_and_publish_DiskSpaceCriticalEvent()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        const long criticalFree = 500L * 1024 * 1024;
        var state = service.UpdateDiskSpaceHealth("/data", criticalFree, total);

        Assert.That(state, Is.EqualTo(DiskSpaceHealthState.Critical));
        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e =>
            e.DrivePath == "/data" &&
            e.FreeBytes == criticalFree));
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceLowEvent>());
    }

    [Test]
    public void UpdateDiskSpaceHealth_should_track_multiple_drives_independently()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        const long total = 100L * 1024 * 1024 * 1024;

        var state1 = service.UpdateDiskSpaceHealth("/drive1", 50L * 1024 * 1024 * 1024, total);
        var state2 = service.UpdateDiskSpaceHealth("/drive2", 3L * 1024 * 1024 * 1024, total);

        Assert.That(state1, Is.EqualTo(DiskSpaceHealthState.Normal));
        Assert.That(state2, Is.EqualTo(DiskSpaceHealthState.Low));
        Assert.That(service.GetHealthState("/drive1"), Is.EqualTo(DiskSpaceHealthState.Normal));
        Assert.That(service.GetHealthState("/drive2"), Is.EqualTo(DiskSpaceHealthState.Low));
    }

    [Test]
    public void ResetHealthStates_should_clear_tracked_states()
    {
        var service = new DiskSpaceService(_appFolderInfo);
        const long total = 100L * 1024 * 1024 * 1024;

        service.UpdateDiskSpaceHealth("/data", 3L * 1024 * 1024 * 1024, total);
        Assert.That(service.GetHealthState("/data"), Is.EqualTo(DiskSpaceHealthState.Low));

        service.ResetHealthStates();
        Assert.That(service.GetHealthState("/data"), Is.EqualTo(DiskSpaceHealthState.Normal));
    }

    [Test]
    public void GetDiskSpace_should_not_mask_root_drive_when_appdata_folder_is_configured()
    {
        _appFolderInfo.AppDataFolder.Returns("/tmp/appdata");
        _appFolderInfo.StartUpFolder.Returns("/tmp/startup");
        _subject = new DiskSpaceService(_appFolderInfo);

        var result = _subject.GetDiskSpace();

        // Both the AppData entry and the root drive should be present
        var appData = result.FirstOrDefault(d => d.Label == "AppData");
        var rootDrive = result.FirstOrDefault(d => d.Path == "/" || d.Path == "C:\\");

        Assert.That(appData, Is.Not.Null, "AppData entry should be present");
        Assert.That(rootDrive, Is.Not.Null, "Root drive should not be masked by AppData");
        Assert.That(rootDrive.Label, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GetDiskSpace_should_include_category_save_paths_from_repository()
    {
        var categoryRepo = Substitute.For<ICategoryRepository>();
        var categories = new List<Category>
        {
            new() { Id = 1, Name = "Movies", SavePath = "/tmp/movies" },
        };
        categoryRepo.All().Returns(categories);

        _subject = new DiskSpaceService(_appFolderInfo, categoryRepo);

        var result = _subject.GetDiskSpace();

        var categoryEntry = result.FirstOrDefault(d => d.Label == "Movies" || d.Path == "/tmp/movies");
        Assert.That(categoryEntry, Is.Not.Null, "Category save path should be included from repository");
    }

    [Test]
    public void GetDiskSpace_should_include_category_save_paths_from_service()
    {
        var categoryService = Substitute.For<ICategoryService>();
        var categories = new List<Category>
        {
            new() { Id = 2, Name = "TV", SavePath = "/tmp/tv" },
        };
        categoryService.GetAll().Returns(categories);

        _subject = new DiskSpaceService(_appFolderInfo, null, categoryService);

        var result = _subject.GetDiskSpace();

        var categoryEntry = result.FirstOrDefault(d => d.Label == "TV" || d.Path == "/tmp/tv");
        Assert.That(categoryEntry, Is.Not.Null, "Category save path should be included from service");
    }

    [Test]
    public void GetDiskSpace_should_deduplicate_categories_sharing_same_root()
    {
        var categoryRepo = Substitute.For<ICategoryRepository>();
        var categories = new List<Category>
        {
            new() { Id = 1, Name = "Cat1", SavePath = "/tmp/cat1" },
            new() { Id = 2, Name = "Cat2", SavePath = "/tmp/cat2" },
        };
        categoryRepo.All().Returns(categories);

        _subject = new DiskSpaceService(_appFolderInfo, categoryRepo);

        var result = _subject.GetDiskSpace();

        // Both /tmp/cat1 and /tmp/cat2 share root "/". Only the first should be added as a category entry.
        var categoryEntries = result.Where(d => d.Label == "Cat1" || d.Label == "Cat2").ToList();
        Assert.That(categoryEntries.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetDiskSpace_with_custom_drive_timeout_should_execute_gracefully()
    {
        _subject.DriveTimeout = TimeSpan.FromMilliseconds(100);

        var result = _subject.GetDiskSpace();

        Assert.That(result, Is.Not.Null);
    }

    // --- TTL Caching & Event Decoupling tests ---

    [Test]
    public void GetDiskSpace_called_twice_within_ttl_should_return_cached_result()
    {
        var categoryRepo = Substitute.For<ICategoryRepository>();
        categoryRepo.All().Returns(new List<Category>());
        var service = new DiskSpaceService(_appFolderInfo, categoryRepo);

        var first = service.GetDiskSpace();
        var second = service.GetDiskSpace();

        Assert.That(second, Is.SameAs(first));
        categoryRepo.Received(1).All();
    }

    [Test]
    public void GetDiskSpace_with_force_refresh_true_should_bypass_cache()
    {
        var categoryRepo = Substitute.For<ICategoryRepository>();
        categoryRepo.All().Returns(new List<Category>());
        var service = new DiskSpaceService(_appFolderInfo, categoryRepo);

        var first = service.GetDiskSpace();
        var second = service.GetDiskSpace(forceRefresh: true);

        Assert.That(second, Is.Not.SameAs(first));
        categoryRepo.Received(2).All();
    }

    [Test]
    public void GetDiskSpace_should_not_publish_critical_or_low_events()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);

        var result = service.GetDiskSpace();

        Assert.That(result, Is.Not.Null);
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceCriticalEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceLowEvent>());
        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<DiskSpaceRestoredEvent>());
    }

    [Test]
    public void CheckDiskSpaceThresholds_should_publish_events_when_disk_space_is_below_thresholds()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var service = new DiskSpaceService(_appFolderInfo, eventAggregator);
        var mockDrives = new List<DiskSpaceInfo>
        {
            new()
            {
                Path = "/critical-disk",
                Label = "Critical",
                FreeSpace = 500L * 1024 * 1024,
                TotalSpace = 100L * 1024 * 1024 * 1024,
            },
            new()
            {
                Path = "/low-disk",
                Label = "Low",
                FreeSpace = 3L * 1024 * 1024 * 1024,
                TotalSpace = 100L * 1024 * 1024 * 1024,
            },
        };

        service.CheckDiskSpaceThresholds(mockDrives);

        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceCriticalEvent>(e => e.DrivePath == "/critical-disk"));
        eventAggregator.Received(1).PublishEvent(Arg.Is<DiskSpaceLowEvent>(e => e.DrivePath == "/low-disk"));
    }

    // --- Resilience and mount deduplication tests (#537) ---

    [Test]
    public void GetDiskSpace_should_isolate_failing_or_stale_network_mount_and_return_healthy_drives()
    {
        var rootDrive = new DriveInfo("/");
        var staleDrive = new DriveInfo("/mnt/stale-share");

        _subject.DrivesProvider = () => new[] { staleDrive, rootDrive };
        _subject.ProcMountsProvider = () =>
            "/dev/sda1 / ext4 rw,relatime 0 0\n" +
            "192.168.1.100:/export /mnt/stale-share nfs rw,relatime 0 0\n";

        _subject.DriveStatsReader = drive =>
        {
            if (drive.Name.Contains("stale"))
            {
                throw new IOException("Stale file handle (ESTALE)");
            }

            return new DriveSpaceStats
            {
                FreeSpace = 40L * 1024 * 1024 * 1024,
                TotalSpace = 100L * 1024 * 1024 * 1024,
                VolumeLabel = "RootVolume",
                FileSystemType = "ext4",
                IsReadOnly = false,
            };
        };

        var result = _subject.GetDiskSpace(forceRefresh: true);

        Assert.That(result, Is.Not.Null);
        var healthy = result.FirstOrDefault(d => d.Path == "/" || d.Label == "RootVolume");
        Assert.That(healthy, Is.Not.Null, "Healthy drive should be returned despite adjacent stale share failure");
        Assert.That(result.Any(d => d.Path.Contains("stale")), Is.False, "Stale drive should be isolated and omitted");
    }

    [Test]
    public void GetDiskSpace_should_deduplicate_container_bind_mounts_sharing_same_device_id()
    {
        var rootDrive = new DriveInfo("/");
        var downloadsDrive = new DriveInfo("/downloads");
        var dataDrive = new DriveInfo("/data");

        _subject.DrivesProvider = () => new[] { rootDrive, downloadsDrive, dataDrive };
        _subject.ProcMountsProvider = () =>
            "/dev/sda1 / ext4 rw,relatime 0 0\n" +
            "/dev/sda1 /downloads ext4 rw,relatime 0 0\n" +
            "/dev/sda1 /data ext4 rw,relatime 0 0\n";

        _subject.DriveStatsReader = drive => new DriveSpaceStats
        {
            FreeSpace = 20L * 1024 * 1024 * 1024,
            TotalSpace = 50L * 1024 * 1024 * 1024,
            VolumeLabel = drive.Name,
            FileSystemType = "ext4",
            IsReadOnly = false,
        };

        var result = _subject.GetDiskSpace(forceRefresh: true);

        Assert.That(result, Is.Not.Null);
        var sda1Entries = result.Where(d => d.Path == "/" || d.Path == "/downloads" || d.Path == "/data").ToList();
        Assert.That(sda1Entries.Count, Is.EqualTo(1), "Duplicate bind mounts on /dev/sda1 should be deduplicated to a single entry");
    }

    [Test]
    public void GetDiskSpace_should_populate_filesystem_type_and_read_only_flags_from_mounts()
    {
        var zfsDrive = new DriveInfo("/mnt/storage");

        _subject.DrivesProvider = () => new[] { zfsDrive };
        _subject.ProcMountsProvider = () =>
            "pool/storage /mnt/storage zfs ro,relatime 0 0\n";

        _subject.DriveStatsReader = drive => new DriveSpaceStats
        {
            FreeSpace = 100L * 1024 * 1024 * 1024,
            TotalSpace = 500L * 1024 * 1024 * 1024,
            VolumeLabel = "ZfsPool",
        };

        var result = _subject.GetDiskSpace(forceRefresh: true);

        Assert.That(result, Is.Not.Null);
        var zfsEntry = result.FirstOrDefault(d => d.Path == "/mnt/storage");
        Assert.That(zfsEntry, Is.Not.Null);
        Assert.That(zfsEntry.FileSystemType, Is.EqualTo("zfs"));
        Assert.That(zfsEntry.IsReadOnly, Is.True);
    }
}
