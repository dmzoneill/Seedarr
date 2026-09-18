using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Update;

public class UpdatePackageProvider : IUpdatePackageProvider
{
    private readonly IEnvironmentProvider _environmentProvider;
    private readonly string _platformOverride;
    private readonly bool? _isDockerOverride;

    public UpdatePackageProvider(
        IEnvironmentProvider environmentProvider = null,
        string platformOverride = null,
        bool? isDockerOverride = null)
    {
        _environmentProvider = environmentProvider;
        _platformOverride = platformOverride;
        _isDockerOverride = isDockerOverride;
    }

    public string CurrentPlatform => _platformOverride ?? ResolveCurrentPlatform();

    public string UpdateMechanism => GetUpdateMechanism();

    public bool IsDocker => IsDockerEnvironment();

    public UpdatePackage ResolvePackage(IEnumerable<ReleaseAsset> assets)
    {
        return ResolvePackage(assets, CurrentPlatform);
    }

    public UpdatePackage ResolvePackage(IEnumerable<ReleaseAsset> assets, string platform)
    {
        if (assets == null)
        {
            return null;
        }

        var targetPlatform = string.IsNullOrWhiteSpace(platform) ? CurrentPlatform : platform.Trim();
        var assetList = assets.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Name)).ToList();

        var packageAsset = assetList.FirstOrDefault(a => IsPackageMatch(a.Name, targetPlatform));
        if (packageAsset == null)
        {
            return null;
        }

        var checksumUrl = ResolveChecksumUrl(assetList, packageAsset.Name, targetPlatform);

        return new UpdatePackage
        {
            FileName = packageAsset.Name,
            DownloadUrl = packageAsset.DownloadUrl,
            Sha256ChecksumUrl = checksumUrl,
            Size = packageAsset.Size,
            Platform = targetPlatform,
        };
    }

    public bool IsReleaseApplicable(bool isPrerelease, string channel)
    {
        if (!isPrerelease)
        {
            return true;
        }

        var ch = (channel ?? "main").Trim().ToLowerInvariant();
        return ch switch
        {
            "develop" or "nightly" => true,
            _ => false,
        };
    }

    public bool IsDockerEnvironment()
    {
        if (_isDockerOverride.HasValue)
        {
            return _isDockerOverride.Value;
        }

        if (_environmentProvider != null)
        {
            return _environmentProvider.IsDocker;
        }

        return EnvironmentProvider.CheckIsDocker();
    }

    public string GetUpdateMechanism()
    {
        return IsDockerEnvironment() ? "Docker" : "BuiltIn";
    }

    public static string ResolveCurrentPlatform()
    {
        var arch = RuntimeInformation.ProcessArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return arch switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                Architecture.Arm => "linux-arm",
                _ => $"linux-{arch.ToString().ToLowerInvariant()}",
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return arch switch
            {
                Architecture.Arm64 => "osx-arm64",
                Architecture.X64 => "osx-x64",
                _ => $"osx-{arch.ToString().ToLowerInvariant()}",
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return arch switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                Architecture.X86 => "win-x86",
                _ => $"win-{arch.ToString().ToLowerInvariant()}",
            };
        }

        return "unknown";
    }

    public static bool IsPackageMatch(string fileName, string platform)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }

        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".sha256") ||
            lower.EndsWith(".sha512") ||
            lower.EndsWith(".md5") ||
            lower.EndsWith(".asc") ||
            lower.EndsWith(".sig") ||
            lower.EndsWith(".txt"))
        {
            return false;
        }

        var plat = platform.ToLowerInvariant();
        return plat switch
        {
            "linux-x64" => (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) && lower.Contains("linux-x64"),
            "linux-arm64" => (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) && (lower.Contains("linux-arm64") || lower.Contains("linux-aarch64")),
            "osx-arm64" => (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) && (lower.Contains("osx-arm64") || lower.Contains("darwin-arm64")),
            "osx-x64" => (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) && (lower.Contains("osx-x64") || lower.Contains("darwin-x64")),
            "win-x64" => lower.EndsWith(".zip") && lower.Contains("win-x64"),
            "win-arm64" => lower.EndsWith(".zip") && lower.Contains("win-arm64"),
            "linux-arm" => (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz")) && lower.Contains("linux-arm") && !lower.Contains("arm64"),
            _ => false,
        };
    }

    private static string ResolveChecksumUrl(List<ReleaseAsset> assets, string packageFileName, string platform)
    {
        var pkgLower = packageFileName.ToLowerInvariant();
        var platLower = platform.ToLowerInvariant();

        var specificChecksum = assets.FirstOrDefault(a =>
        {
            var aLower = a.Name.ToLowerInvariant();
            if (aLower == $"{pkgLower}.sha256" || aLower == $"{pkgLower}.sha256.txt")
            {
                return true;
            }

            if (aLower.EndsWith(".sha256") && aLower.Contains(platLower))
            {
                return true;
            }

            return false;
        });

        if (specificChecksum != null)
        {
            return specificChecksum.DownloadUrl;
        }

        var globalChecksum = assets.FirstOrDefault(a =>
        {
            var aLower = a.Name.ToLowerInvariant();
            return aLower == "sha256sums.txt" || aLower == "checksums.txt" || aLower == "sha256sums" || aLower == "checksums.sha256";
        });

        return globalChecksum?.DownloadUrl;
    }
}
