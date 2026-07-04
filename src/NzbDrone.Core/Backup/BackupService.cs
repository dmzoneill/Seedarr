using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Backup;

public interface IBackupService
{
    BackupInfo CreateBackup();
    List<BackupInfo> GetBackups();
    void DeleteBackup(string fileName);
    Stream GetBackupStream(string fileName);
    void RestoreBackup(string fileName);
}

public class BackupService : IBackupService
{
    private const string BackupFolderName = "Backups";
    private const string DbFileName = "seedarr.db";
    private const string ConfigFileName = "config.xml";

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    public BackupService(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public BackupInfo CreateBackup()
    {
        var backupFolder = GetBackupFolder();
        Directory.CreateDirectory(backupFolder);

        var version = BuildInfo.Version.ToString();
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var backupFileName = $"seedarr_backup_{version}_{timestamp}.zip";
        var backupPath = Path.Combine(backupFolder, backupFileName);
        var dbPath = Path.Combine(_appFolderInfo.AppDataFolder, DbFileName);
        var configPath = Path.Combine(_appFolderInfo.AppDataFolder, ConfigFileName);

        if (!File.Exists(dbPath))
        {
            _logger.Warn("Database file not found at {0}, skipping backup", dbPath);
            return null;
        }

        using (var zip = ZipFile.Open(backupPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(dbPath, DbFileName);

            if (File.Exists(configPath))
            {
                zip.CreateEntryFromFile(configPath, ConfigFileName);
            }
        }

        _logger.Info("Backup created: {0}", backupPath);

        var fileInfo = new FileInfo(backupPath);

        return new BackupInfo
        {
            Name = fileInfo.Name,
            Path = fileInfo.FullName,
            Size = fileInfo.Length,
            Time = fileInfo.CreationTimeUtc
        };
    }

    public List<BackupInfo> GetBackups()
    {
        var backupFolder = GetBackupFolder();

        if (!Directory.Exists(backupFolder))
        {
            return new List<BackupInfo>();
        }

        return Directory.GetFiles(backupFolder, "seedarr_backup_*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo
            {
                Name = f.Name,
                Path = f.FullName,
                Size = f.Length,
                Time = f.CreationTimeUtc
            })
            .ToList();
    }

    public void DeleteBackup(string fileName)
    {
        var filePath = GetSafeBackupPath(fileName);

        if (!File.Exists(filePath))
        {
            _logger.Warn("Backup file not found: {0}", filePath);
            return;
        }

        File.Delete(filePath);
        _logger.Info("Backup deleted: {0}", filePath);
    }

    public Stream GetBackupStream(string fileName)
    {
        var filePath = GetSafeBackupPath(fileName);

        if (!File.Exists(filePath))
        {
            return null;
        }

        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void RestoreBackup(string fileName)
    {
        var filePath = GetSafeBackupPath(fileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Backup file not found", fileName);
        }

        _logger.Warn("Restoring backup from {0} - this will overwrite the current database", filePath);

        var dbPath = Path.Combine(_appFolderInfo.AppDataFolder, DbFileName);
        var configPath = Path.Combine(_appFolderInfo.AppDataFolder, ConfigFileName);

        using var zip = ZipFile.OpenRead(filePath);

        var dbEntry = zip.GetEntry(DbFileName);
        if (dbEntry != null)
        {
            dbEntry.ExtractToFile(dbPath, overwrite: true);
            _logger.Info("Database restored from backup");
        }

        var configEntry = zip.GetEntry(ConfigFileName);
        if (configEntry != null)
        {
            configEntry.ExtractToFile(configPath, overwrite: true);
            _logger.Info("Config restored from backup");
        }
    }

    private string GetBackupFolder()
    {
        return Path.Combine(_appFolderInfo.AppDataFolder, BackupFolderName);
    }

    private string GetSafeBackupPath(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        return Path.Combine(GetBackupFolder(), safeName);
    }
}

public class BackupInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public DateTime Time { get; set; }
}
