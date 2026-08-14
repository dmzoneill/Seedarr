using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Backup;

public interface IBackupService
{
    string CreateBackup();
    List<BackupInfo> GetBackups();
    void DeleteBackup(string fileName);
}

public class BackupService : IBackupService
{
    private const string BackupFolderName = "Backups";
    private const string DbFileName = "seedarr.db";

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    public BackupService(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public string CreateBackup()
    {
        var backupFolder = GetBackupFolder();
        Directory.CreateDirectory(backupFolder);

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupFileName = $"seedarr_backup_{timestamp}.db";
        var backupPath = Path.Combine(backupFolder, backupFileName);
        var dbPath = Path.Combine(_appFolderInfo.AppDataFolder, DbFileName);

        if (!File.Exists(dbPath))
        {
            _logger.Warn("Database file not found at {0}, skipping backup", dbPath);
            return null;
        }

        File.Copy(dbPath, backupPath, overwrite: true);
        _logger.Info("Backup created: {0}", backupPath);

        return backupFileName;
    }

    public List<BackupInfo> GetBackups()
    {
        var backupFolder = GetBackupFolder();

        if (!Directory.Exists(backupFolder))
        {
            return new List<BackupInfo>();
        }

        return Directory.GetFiles(backupFolder, "seedarr_backup_*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo
            {
                FileName = f.Name,
                Size = f.Length,
                CreatedAt = f.CreationTimeUtc
            })
            .ToList();
    }

    public void DeleteBackup(string fileName)
    {
        var backupFolder = GetBackupFolder();
        var filePath = Path.Combine(backupFolder, fileName);

        if (!File.Exists(filePath))
        {
            _logger.Warn("Backup file not found: {0}", filePath);
            return;
        }

        File.Delete(filePath);
        _logger.Info("Backup deleted: {0}", filePath);
    }

    private string GetBackupFolder()
    {
        return Path.Combine(_appFolderInfo.AppDataFolder, BackupFolderName);
    }
}

public class BackupInfo
{
    public string FileName { get; set; }
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
}
