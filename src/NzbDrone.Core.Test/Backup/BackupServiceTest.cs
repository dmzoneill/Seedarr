using System;
using System.IO;
using System.IO.Compression;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Backup;

[TestFixture]
public class BackupServiceTest
{
    private IAppFolderInfo _appFolderInfo;
    private IConnectionStringFactory _connectionStringFactory;
    private BackupService _subject;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_backup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);

        _connectionStringFactory = Substitute.For<IConnectionStringFactory>();
        _connectionStringFactory.DatabaseType.Returns(DatabaseType.PostgreSQL);
        _connectionStringFactory.MainDbConnectionString.Returns("Host=localhost;Database=seedarr");

        _subject = new BackupService(_appFolderInfo, _connectionStringFactory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void CreateBackup_should_return_null_when_db_file_not_found()
    {
        var result = _subject.CreateBackup();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void CreateBackup_should_create_zip_when_db_exists()
    {
        File.WriteAllText(Path.Combine(_tempDir, "seedarr.db"), "test db content");

        var result = _subject.CreateBackup();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Does.StartWith("seedarr_backup_"));
        Assert.That(result.Name, Does.EndWith(".zip"));
        Assert.That(File.Exists(result.Path), Is.True);
    }

    [Test]
    public void CreateBackup_should_include_config_when_it_exists()
    {
        File.WriteAllText(Path.Combine(_tempDir, "seedarr.db"), "test db");
        File.WriteAllText(Path.Combine(_tempDir, "config.xml"), "<config />");

        var result = _subject.CreateBackup();

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("config.xml"), Is.Not.Null);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Not.Null);
    }

    [Test]
    public void CreateBackup_should_work_without_config_file()
    {
        File.WriteAllText(Path.Combine(_tempDir, "seedarr.db"), "test db");

        var result = _subject.CreateBackup();

        using var zip = ZipFile.OpenRead(result.Path);
        Assert.That(zip.GetEntry("seedarr.db"), Is.Not.Null);
        Assert.That(zip.GetEntry("config.xml"), Is.Null);
    }

    [Test]
    public void GetBackups_should_return_empty_list_when_folder_does_not_exist()
    {
        var result = _subject.GetBackups();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetBackups_should_return_backup_files()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "seedarr_backup_1.0_2024-01-01.zip"), "fake zip");

        var result = _subject.GetBackups();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Does.Contain("seedarr_backup_"));
    }

    [Test]
    public void DeleteBackup_should_do_nothing_when_file_not_found()
    {
        Assert.DoesNotThrow(() => _subject.DeleteBackup("nonexistent.zip"));
    }

    [Test]
    public void DeleteBackup_should_delete_the_file()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var filePath = Path.Combine(backupDir, "test.zip");
        File.WriteAllText(filePath, "content");

        _subject.DeleteBackup("test.zip");

        Assert.That(File.Exists(filePath), Is.False);
    }

    [Test]
    public void GetBackupStream_should_return_null_when_file_not_found()
    {
        var result = _subject.GetBackupStream("nonexistent.zip");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetBackupStream_should_return_stream_when_file_exists()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "test.zip"), "content");

        using var result = _subject.GetBackupStream("test.zip");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.CanRead, Is.True);
    }

    [Test]
    public void RestoreBackup_should_throw_when_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() => _subject.RestoreBackup("nonexistent.zip"));
    }

    [Test]
    public void RestoreBackup_should_extract_db_file()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, "restore_test.zip");

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("seedarr.db");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("restored db content");
        }

        _subject.RestoreBackup("restore_test.zip");

        var dbRestorePath = Path.Combine(_tempDir, "seedarr.db.restore");
        Assert.That(File.Exists(dbRestorePath), Is.True);
        Assert.That(File.ReadAllText(dbRestorePath), Is.EqualTo("restored db content"));
    }

    [Test]
    public void DeleteBackup_should_strip_directory_traversal()
    {
        var backupDir = Path.Combine(_tempDir, "Backups");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(Path.Combine(backupDir, "safe.zip"), "content");

        Assert.DoesNotThrow(() => _subject.DeleteBackup("../../../etc/passwd"));
    }
}
