using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Update;

public class InstallUpdateService : IInstallUpdateService
{
    private readonly IUpdateService _updateService;
    private readonly IUpdatePackageProvider _updatePackageProvider;
    private readonly IPostUpdateVerificationService _postUpdateVerificationService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IEnvironmentProvider _environmentProvider;
    private readonly HttpClient _httpClient;
    private readonly string _installDirectory;
    private readonly bool? _isContainerizedOverride;
    private readonly Logger _logger;

    private readonly object _lock = new();
    private UpdateInstallProgress _progress = new();
    private Task<bool> _ongoingTask;

    public InstallUpdateService(
        IUpdateService updateService = null,
        IUpdatePackageProvider updatePackageProvider = null,
        IPostUpdateVerificationService postUpdateVerificationService = null,
        IAppFolderInfo appFolderInfo = null,
        HttpClient httpClient = null,
        IEnvironmentProvider environmentProvider = null,
        string installDirectory = null,
        bool? isContainerizedOverride = null)
    {
        _updateService = updateService;
        _updatePackageProvider = updatePackageProvider ?? new UpdatePackageProvider(environmentProvider);
        _postUpdateVerificationService = postUpdateVerificationService;
        _appFolderInfo = appFolderInfo;
        _environmentProvider = environmentProvider;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _installDirectory = installDirectory ?? _appFolderInfo?.StartUpFolder ?? AppDomain.CurrentDomain.BaseDirectory;
        _isContainerizedOverride = isContainerizedOverride;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsContainerized => CheckIsContainerized();

    public string InstallDirectory => _installDirectory;

    public UpdateInstallProgress GetProgress()
    {
        lock (_lock)
        {
            return new UpdateInstallProgress
            {
                Stage = _progress.Stage,
                Percentage = _progress.Percentage,
                ErrorMessage = _progress.ErrorMessage,
                TargetVersion = _progress.TargetVersion,
            };
        }
    }

    public Task<bool> InstallUpdateAsync(string version = null, CancellationToken cancellationToken = default)
    {
        if (IsContainerized)
        {
            var containerMsg = "In-app updates are not supported in container environments. Please update via your container manager.";
            SetProgress(UpdateInstallStage.Failed, 0, containerMsg, version);
            throw new InvalidOperationException(containerMsg);
        }

        lock (_lock)
        {
            if (_ongoingTask != null && !_ongoingTask.IsCompleted)
            {
                return _ongoingTask;
            }

            _ongoingTask = ExecuteInstallAsync(version, cancellationToken);
            return _ongoingTask;
        }
    }

    public async Task<bool> StageAndInstallAsync(
        string packageFilePath,
        string expectedChecksum = null,
        string targetVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (IsContainerized)
        {
            var containerMsg = "In-app updates are not supported in container environments. Please update via your container manager.";
            SetProgress(UpdateInstallStage.Failed, 0, containerMsg, targetVersion);
            throw new InvalidOperationException(containerMsg);
        }

        try
        {
            // 1. Verify SHA-256
            SetProgress(UpdateInstallStage.Verifying, 40, null, targetVersion);
            if (!string.IsNullOrWhiteSpace(expectedChecksum))
            {
                var actualChecksum = ComputeSha256(packageFilePath);
                if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    var mismatchMsg = $"SHA-256 verification failed for {Path.GetFileName(packageFilePath)}. Expected: {expectedChecksum}, got: {actualChecksum}.";
                    _logger.Error(mismatchMsg);
                    SetProgress(UpdateInstallStage.Failed, 40, mismatchMsg, targetVersion);
                    throw new InvalidOperationException(mismatchMsg);
                }
            }

            SetProgress(UpdateInstallStage.Verifying, 50, null, targetVersion);

            // 2. Stage extraction to temporary directory
            SetProgress(UpdateInstallStage.Extracting, 60, null, targetVersion);
            var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_staging_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            ExtractPackage(packageFilePath, tempDir);
            SetProgress(UpdateInstallStage.Extracting, 75, null, targetVersion);

            // 3. Safely stage executable replacements with ETXTBSY handling
            SetProgress(UpdateInstallStage.Installing, 80, null, targetVersion);
            StageInstallation(tempDir, targetVersion);
            SetProgress(UpdateInstallStage.Installing, 95, null, targetVersion);

            // 4. Transition to RestartRequired
            SetProgress(UpdateInstallStage.RestartRequired, 100, null, targetVersion);
            _logger.Info("Update to version {0} staged successfully. Restart required.", targetVersion);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to stage and install package for {0}", targetVersion);
            lock (_lock)
            {
                if (_progress.Stage != UpdateInstallStage.Failed)
                {
                    SetProgress(UpdateInstallStage.Failed, _progress.Percentage, ex.Message, targetVersion);
                }
            }

            throw;
        }
    }

    private async Task<bool> ExecuteInstallAsync(string version, CancellationToken cancellationToken)
    {
        SetProgress(UpdateInstallStage.Downloading, 0, null, version);

        try
        {
            // 1. Resolve update package
            var (targetVersion, package) = await ResolvePackageAsync(version, cancellationToken).ConfigureAwait(false);
            if (package == null || string.IsNullOrWhiteSpace(package.DownloadUrl))
            {
                var errorMsg = $"No suitable update package found for version '{version ?? "latest"}' on platform '{_updatePackageProvider.CurrentPlatform}'.";
                SetProgress(UpdateInstallStage.Failed, 0, errorMsg, version);
                throw new InvalidOperationException(errorMsg);
            }

            SetProgress(UpdateInstallStage.Downloading, 10, null, targetVersion);

            // 2. Download package to temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_update_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var packageFilePath = Path.Combine(tempDir, package.FileName ?? "update_package");
            _logger.Info("Downloading update package from '{0}' to '{1}'...", package.DownloadUrl, packageFilePath);

            await DownloadFileAsync(package.DownloadUrl, packageFilePath, cancellationToken).ConfigureAwait(false);
            SetProgress(UpdateInstallStage.Downloading, 35, null, targetVersion);

            // 3. Download checksum if available
            string expectedChecksum = null;
            if (!string.IsNullOrWhiteSpace(package.Sha256ChecksumUrl))
            {
                _logger.Info("Downloading SHA-256 checksum from '{0}'...", package.Sha256ChecksumUrl);
                var checksumContent = await _httpClient.GetStringAsync(package.Sha256ChecksumUrl, cancellationToken).ConfigureAwait(false);
                expectedChecksum = ExtractChecksum(checksumContent, package.FileName);
            }

            // 4. Verify SHA-256
            SetProgress(UpdateInstallStage.Verifying, 40, null, targetVersion);
            if (!string.IsNullOrWhiteSpace(expectedChecksum))
            {
                var actualChecksum = ComputeSha256(packageFilePath);
                if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    var errorMsg = $"SHA-256 verification failed for {package.FileName}. Expected: {expectedChecksum}, got: {actualChecksum}.";
                    _logger.Error(errorMsg);
                    SetProgress(UpdateInstallStage.Failed, 40, errorMsg, targetVersion);
                    throw new InvalidOperationException(errorMsg);
                }

                _logger.Info("SHA-256 verification passed for {0}: {1}", package.FileName, actualChecksum);
            }

            SetProgress(UpdateInstallStage.Verifying, 50, null, targetVersion);

            // 5. Stage extraction to temporary directory
            SetProgress(UpdateInstallStage.Extracting, 60, null, targetVersion);
            var stagingDir = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(stagingDir);

            ExtractPackage(packageFilePath, stagingDir);
            SetProgress(UpdateInstallStage.Extracting, 75, null, targetVersion);

            // 6. Safely stage executable replacements with ETXTBSY handling
            SetProgress(UpdateInstallStage.Installing, 80, null, targetVersion);
            StageInstallation(stagingDir, targetVersion);
            SetProgress(UpdateInstallStage.Installing, 95, null, targetVersion);

            // 7. Cleanup temp package file
            try
            {
                if (File.Exists(packageFilePath))
                {
                    File.Delete(packageFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to delete temporary package file '{0}'", packageFilePath);
            }

            // 8. Transition to RestartRequired
            SetProgress(UpdateInstallStage.RestartRequired, 100, null, targetVersion);
            _logger.Info("Update to version {0} staged successfully. Restart required.", targetVersion);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to install update {0}", version);
            lock (_lock)
            {
                if (_progress.Stage != UpdateInstallStage.Failed)
                {
                    SetProgress(UpdateInstallStage.Failed, _progress.Percentage, ex.Message, version);
                }
            }

            throw;
        }
    }

    private async Task<(string targetVersion, UpdatePackage package)> ResolvePackageAsync(string version, CancellationToken cancellationToken)
    {
        if (_updateService == null)
        {
            return (version ?? "1.0.0", null);
        }

        var updateInfo = await _updateService.CheckForUpdateAsync(false, cancellationToken).ConfigureAwait(false);
        if (updateInfo == null)
        {
            return (version, null);
        }

        var releases = updateInfo.Releases ?? new List<ReleaseInfo>();

        ReleaseInfo matchedRelease;
        if (!string.IsNullOrWhiteSpace(version))
        {
            matchedRelease = releases.FirstOrDefault(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(r.Version?.TrimStart('v', 'V'), version.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            matchedRelease = releases.FirstOrDefault(r => string.Equals(r.Version, updateInfo.LatestVersion, StringComparison.OrdinalIgnoreCase)) ??
                             releases.FirstOrDefault();
        }

        var targetVer = matchedRelease?.Version ?? version ?? updateInfo.LatestVersion ?? "latest";
        UpdatePackage package = null;

        if (matchedRelease?.Assets != null && matchedRelease.Assets.Count > 0)
        {
            package = _updatePackageProvider.ResolvePackage(matchedRelease.Assets);
        }

        if (package == null && updateInfo.Package != null && (string.IsNullOrWhiteSpace(version) || string.Equals(version, updateInfo.LatestVersion, StringComparison.OrdinalIgnoreCase)))
        {
            package = updateInfo.Package;
        }

        return (targetVer, package);
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var pct = 10 + (int)(25.0 * totalRead / totalBytes.Value);
                SetProgress(UpdateInstallStage.Downloading, Math.Min(35, pct));
            }
        }
    }

    public void ExtractPackage(string packageFilePath, string stagingDir)
    {
        var lower = packageFilePath.ToLowerInvariant();
        if (lower.EndsWith(".zip"))
        {
            ZipFile.ExtractToDirectory(packageFilePath, stagingDir, overwriteFiles: true);
        }
        else if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
        {
            using var fs = File.OpenRead(packageFilePath);
            using var gzip = new GZipStream(fs, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, stagingDir, overwriteFiles: true);
        }
        else if (lower.EndsWith(".tar"))
        {
            TarFile.ExtractToDirectory(packageFilePath, stagingDir, overwriteFiles: true);
        }
        else
        {
            var dest = Path.Combine(stagingDir, Path.GetFileName(packageFilePath));
            File.Copy(packageFilePath, dest, overwrite: true);
        }
    }

    public void StageInstallation(string stagingDir, string targetVersion)
    {
        var sourceDir = stagingDir;
        var subDirs = Directory.GetDirectories(stagingDir);
        var subFiles = Directory.GetFiles(stagingDir);
        if (subDirs.Length == 1 && subFiles.Length == 0)
        {
            sourceDir = subDirs[0];
        }

        var backupDir = Path.Combine(Path.GetTempPath(), "seedarr_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDir);

        var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        int totalFiles = allFiles.Length;
        int processed = 0;

        foreach (var file in allFiles)
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var targetFile = Path.Combine(_installDirectory, relativePath);

            if (File.Exists(targetFile))
            {
                var backupFile = Path.Combine(backupDir, relativePath);
                var backupParent = Path.GetDirectoryName(backupFile);
                if (!string.IsNullOrEmpty(backupParent) && !Directory.Exists(backupParent))
                {
                    Directory.CreateDirectory(backupParent);
                }

                File.Copy(targetFile, backupFile, overwrite: true);
            }

            SafelyReplaceFile(file, targetFile);

            processed++;
            var pct = 80 + (int)(15.0 * processed / Math.Max(1, totalFiles));
            SetProgress(UpdateInstallStage.Installing, Math.Min(95, pct), targetVersion: targetVersion);
        }

        _postUpdateVerificationService?.StageUpdate(targetVersion, backupDir);
    }

    public static void SafelyReplaceFile(string sourceFile, string destinationFile)
    {
        var destDir = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(destinationFile))
        {
            var oldFile = destinationFile + ".old";
            if (File.Exists(oldFile))
            {
                try
                {
                    File.Delete(oldFile);
                }
                catch
                {
                    oldFile = destinationFile + ".old." + Guid.NewGuid().ToString("N");
                }
            }

            // POSIX rename allows moving currently running executables to avoid ETXTBSY
            File.Move(destinationFile, oldFile);
        }

        File.Copy(sourceFile, destinationFile, overwrite: true);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var fileName = Path.GetFileName(destinationFile);
                if (!fileName.Contains('.') || fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
                {
                    File.SetUnixFileMode(destinationFile,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
            catch
            {
                // Ignore errors on filesystems or OS not supporting Unix file modes
            }
        }
    }

    public static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string ExtractChecksum(string checksumContent, string packageFileName)
    {
        if (string.IsNullOrWhiteSpace(checksumContent))
        {
            return null;
        }

        var lines = checksumContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(packageFileName))
        {
            foreach (var line in lines)
            {
                if (line.Contains(packageFileName, StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(line, @"\b[a-fA-F0-9]{64}\b");
                    if (match.Success)
                    {
                        return match.Value.ToLowerInvariant();
                    }
                }
            }
        }

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"\b[a-fA-F0-9]{64}\b");
            if (match.Success)
            {
                return match.Value.ToLowerInvariant();
            }
        }

        return null;
    }

    private bool CheckIsContainerized()
    {
        if (_isContainerizedOverride.HasValue)
        {
            return _isContainerizedOverride.Value;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (File.Exists("/.dockerenv"))
        {
            return true;
        }

        if (Environment.GetEnvironmentVariable("SEEDARR_IN_DOCKER") != null)
        {
            return true;
        }

        if (_environmentProvider != null && _environmentProvider.IsDocker)
        {
            return true;
        }

        return false;
    }

    private void SetProgress(UpdateInstallStage stage, int percentage, string errorMessage = null, string targetVersion = null)
    {
        lock (_lock)
        {
            _progress = new UpdateInstallProgress
            {
                Stage = stage,
                Percentage = percentage,
                ErrorMessage = errorMessage,
                TargetVersion = targetVersion ?? _progress.TargetVersion,
            };
        }
    }
}
