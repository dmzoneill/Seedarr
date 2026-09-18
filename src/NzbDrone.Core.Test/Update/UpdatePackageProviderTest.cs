using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.Update;

[TestFixture]
public class UpdatePackageProviderTest
{
    [Test]
    public void ResolvePackage_should_resolve_linux_x64_package_and_companion_checksum()
    {
        var provider = new UpdatePackageProvider(platformOverride: "linux-x64");
        var assets = new List<ReleaseAsset>
        {
            new("seedarr-v1.2.0-linux-x64.tar.gz", "https://github.com/test/seedarr-v1.2.0-linux-x64.tar.gz", 50000000),
            new("seedarr-v1.2.0-linux-x64.tar.gz.sha256", "https://github.com/test/seedarr-v1.2.0-linux-x64.tar.gz.sha256", 64),
            new("seedarr-v1.2.0-win-x64.zip", "https://github.com/test/seedarr-v1.2.0-win-x64.zip", 45000000),
        };

        var package = provider.ResolvePackage(assets);

        Assert.That(package, Is.Not.Null);
        Assert.That(package.FileName, Is.EqualTo("seedarr-v1.2.0-linux-x64.tar.gz"));
        Assert.That(package.DownloadUrl, Is.EqualTo("https://github.com/test/seedarr-v1.2.0-linux-x64.tar.gz"));
        Assert.That(package.Sha256ChecksumUrl, Is.EqualTo("https://github.com/test/seedarr-v1.2.0-linux-x64.tar.gz.sha256"));
        Assert.That(package.Size, Is.EqualTo(50000000));
        Assert.That(package.Platform, Is.EqualTo("linux-x64"));
    }

    [TestCase("seedarr-v1.2.0-linux-arm64.tar.gz", "linux-arm64")]
    [TestCase("seedarr-v1.2.0-linux-aarch64.tar.gz", "linux-arm64")]
    [TestCase("seedarr-v1.2.0-osx-arm64.tar.gz", "osx-arm64")]
    [TestCase("seedarr-v1.2.0-darwin-arm64.tar.gz", "osx-arm64")]
    [TestCase("seedarr-v1.2.0-osx-x64.tar.gz", "osx-x64")]
    [TestCase("seedarr-v1.2.0-darwin-x64.tar.gz", "osx-x64")]
    [TestCase("seedarr-v1.2.0-win-x64.zip", "win-x64")]
    public void ResolvePackage_should_match_supported_architectures_and_platforms(string assetName, string platform)
    {
        var provider = new UpdatePackageProvider(platformOverride: platform);
        var assets = new List<ReleaseAsset>
        {
            new(assetName, $"https://github.com/test/{assetName}", 40000000),
            new($"{assetName}.sha256", $"https://github.com/test/{assetName}.sha256", 64),
        };

        var package = provider.ResolvePackage(assets);

        Assert.That(package, Is.Not.Null);
        Assert.That(package.FileName, Is.EqualTo(assetName));
        Assert.That(package.Platform, Is.EqualTo(platform));
        Assert.That(package.Sha256ChecksumUrl, Is.EqualTo($"https://github.com/test/{assetName}.sha256"));
    }

    [Test]
    public void ResolvePackage_should_not_match_windows_tar_gz()
    {
        var provider = new UpdatePackageProvider(platformOverride: "win-x64");
        var assets = new List<ReleaseAsset>
        {
            new("seedarr-win-x64.tar.gz", "https://github.com/test/seedarr-win-x64.tar.gz", 30000000),
        };

        var package = provider.ResolvePackage(assets);

        Assert.That(package, Is.Null);
    }

    [Test]
    public void ResolvePackage_should_fallback_to_global_checksum_file()
    {
        var provider = new UpdatePackageProvider(platformOverride: "linux-x64");
        var assets = new List<ReleaseAsset>
        {
            new("seedarr-linux-x64.tar.gz", "https://github.com/test/seedarr-linux-x64.tar.gz", 50000000),
            new("sha256sums.txt", "https://github.com/test/sha256sums.txt", 256),
        };

        var package = provider.ResolvePackage(assets);

        Assert.That(package, Is.Not.Null);
        Assert.That(package.Sha256ChecksumUrl, Is.EqualTo("https://github.com/test/sha256sums.txt"));
    }

    [Test]
    public void ResolvePackage_should_return_null_when_no_matching_asset_found()
    {
        var provider = new UpdatePackageProvider(platformOverride: "linux-x64");
        var assets = new List<ReleaseAsset>
        {
            new("seedarr-win-x64.zip", "https://github.com/test/seedarr-win-x64.zip", 45000000),
            new("seedarr-osx-arm64.tar.gz", "https://github.com/test/seedarr-osx-arm64.tar.gz", 40000000),
        };

        var package = provider.ResolvePackage(assets);

        Assert.That(package, Is.Null);
    }

    [Test]
    public void ResolvePackage_should_return_null_checksum_when_no_checksum_exists()
    {
        var provider = new UpdatePackageProvider(platformOverride: "linux-x64");
        var assets = new List<ReleaseAsset>
        {
            new("seedarr-linux-x64.tar.gz", "https://github.com/test/seedarr-linux-x64.tar.gz", 50000000),
        };

        var package = provider.ResolvePackage(assets);

        Assert.That(package, Is.Not.Null);
        Assert.That(package.Sha256ChecksumUrl, Is.Null);
    }

    [TestCase("main", true, false)]
    [TestCase("main", false, true)]
    [TestCase("stable", true, false)]
    [TestCase("stable", false, true)]
    [TestCase("develop", true, true)]
    [TestCase("develop", false, true)]
    [TestCase("nightly", true, true)]
    [TestCase("nightly", false, true)]
    [TestCase(null, true, false)]
    [TestCase(null, false, true)]
    public void IsReleaseApplicable_should_filter_by_channel_and_prerelease(string channel, bool isPrerelease, bool expected)
    {
        var provider = new UpdatePackageProvider();

        var result = provider.IsReleaseApplicable(isPrerelease, channel);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Docker_guard_when_containerized_should_report_docker_mechanism()
    {
        var provider = new UpdatePackageProvider(isDockerOverride: true);

        Assert.That(provider.IsDocker, Is.True);
        Assert.That(provider.UpdateMechanism, Is.EqualTo("Docker"));
    }

    [Test]
    public void Docker_guard_when_bare_metal_should_report_builtin_mechanism()
    {
        var provider = new UpdatePackageProvider(isDockerOverride: false);

        Assert.That(provider.IsDocker, Is.False);
        Assert.That(provider.UpdateMechanism, Is.EqualTo("BuiltIn"));
    }

    [Test]
    public void EnvironmentProvider_detects_docker_via_env_var()
    {
        var prev = Environment.GetEnvironmentVariable("SEEDARR_IN_DOCKER");
        try
        {
            Environment.SetEnvironmentVariable("SEEDARR_IN_DOCKER", "1");
            Assert.That(EnvironmentProvider.CheckIsDocker(), Is.True);

            Environment.SetEnvironmentVariable("SEEDARR_IN_DOCKER", null);
            // In case running outside real docker container
            if (!System.IO.File.Exists("/.dockerenv") &&
                !string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.That(EnvironmentProvider.CheckIsDocker(), Is.False);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEEDARR_IN_DOCKER", prev);
        }
    }
}
