using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using Seedarr.Api.V1.Update;

namespace NzbDrone.Core.Test.Update;

[TestFixture]
public class UpdateControllerTest
{
    private IUpdateService _updateService;
    private IPostUpdateVerificationService _postUpdateVerificationService;
    private UpdateController _controller;

    [SetUp]
    public void SetUp()
    {
        _updateService = Substitute.For<IUpdateService>();
        _postUpdateVerificationService = Substitute.For<IPostUpdateVerificationService>();
        _controller = new UpdateController(_updateService, _postUpdateVerificationService);
    }

    private static bool InvokeAreVersionsEqual(Version a, Version b)
    {
        var method = typeof(UpdateController).GetMethod("AreVersionsEqual", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method.Invoke(null, new object[] { a, b });
    }

    [Test]
    public void AreVersionsEqual_should_return_true_for_identical_versions()
    {
        var v1 = new Version(1, 2, 3);
        var v2 = new Version(1, 2, 3);

        Assert.That(InvokeAreVersionsEqual(v1, v2), Is.True);
    }

    [Test]
    public void AreVersionsEqual_should_return_true_for_3part_and_4part_equivalent_versions()
    {
        var threePart = new Version(1, 0, 0);
        var fourPart = new Version(1, 0, 0, 0);

        Assert.That(InvokeAreVersionsEqual(threePart, fourPart), Is.True);
        Assert.That(InvokeAreVersionsEqual(fourPart, threePart), Is.True);
    }

    [Test]
    public void AreVersionsEqual_should_return_true_for_2part_and_4part_equivalent_versions()
    {
        var twoPart = new Version(2, 0);
        var fourPart = new Version(2, 0, 0, 0);

        Assert.That(InvokeAreVersionsEqual(twoPart, fourPart), Is.True);
        Assert.That(InvokeAreVersionsEqual(fourPart, twoPart), Is.True);
    }

    [Test]
    public void AreVersionsEqual_should_return_false_for_different_major()
    {
        var v1 = new Version(1, 0, 0);
        var v2 = new Version(2, 0, 0);

        Assert.That(InvokeAreVersionsEqual(v1, v2), Is.False);
    }

    [Test]
    public void AreVersionsEqual_should_return_false_for_different_minor()
    {
        var v1 = new Version(1, 0, 0);
        var v2 = new Version(1, 1, 0);

        Assert.That(InvokeAreVersionsEqual(v1, v2), Is.False);
    }

    [Test]
    public void AreVersionsEqual_should_return_false_for_different_build()
    {
        var v1 = new Version(1, 0, 1);
        var v2 = new Version(1, 0, 0, 0);

        Assert.That(InvokeAreVersionsEqual(v1, v2), Is.False);
    }

    [Test]
    public void AreVersionsEqual_should_return_false_for_non_zero_revision()
    {
        var v1 = new Version(1, 0, 0);
        var v2 = new Version(1, 0, 0, 1);

        Assert.That(InvokeAreVersionsEqual(v1, v2), Is.False);
    }

    [Test]
    public void AreVersionsEqual_should_return_false_when_either_is_null()
    {
        var v = new Version(1, 0, 0);

        Assert.That(InvokeAreVersionsEqual(null, v), Is.False);
        Assert.That(InvokeAreVersionsEqual(v, null), Is.False);
        Assert.That(InvokeAreVersionsEqual(null, null), Is.False);
    }

    [Test]
    public async Task GetUpdates_should_mark_installed_true_for_equivalent_3part_and_4part_versions()
    {
        var major = BuildInfo.Version.Major;
        var minor = BuildInfo.Version.Minor;
        var build = Math.Max(0, BuildInfo.Version.Build);

        var threePart = $"{major}.{minor}.{build}";
        var fourPart = $"{major}.{minor}.{build}.0";

        var updateInfo = new UpdateInfo
        {
            CurrentVersion = BuildInfo.Version.ToString(),
            LatestVersion = "9.9.9",
            UpdateAvailable = true,
            Releases = new List<ReleaseInfo>
            {
                new() { Version = fourPart, PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = threePart, PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = "9.9.9", PublishedAt = DateTime.UtcNow, Body = "notes" },
            },
        };

        _updateService.CheckForUpdateAsync(false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updateInfo));

        var actionResult = await _controller.GetUpdates();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var results = okResult.Value as List<UpdateResource>;
        Assert.That(results, Is.Not.Null);

        var fourPartRelease = results.Find(r => r.Version == fourPart);
        Assert.That(fourPartRelease, Is.Not.Null);
        Assert.That(fourPartRelease.Installed, Is.True);

        var threePartRelease = results.Find(r => r.Version == threePart);
        Assert.That(threePartRelease, Is.Not.Null);
        Assert.That(threePartRelease.Installed, Is.True);
    }

    [Test]
    public async Task GetUpdates_should_mark_installed_false_for_non_matching_versions()
    {
        var updateInfo = new UpdateInfo
        {
            CurrentVersion = BuildInfo.Version.ToString(),
            LatestVersion = "99.0.0",
            UpdateAvailable = true,
            Releases = new List<ReleaseInfo>
            {
                new() { Version = "99.0.0", PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = "0.0.1", PublishedAt = DateTime.UtcNow, Body = "notes" },
            },
        };

        _updateService.CheckForUpdateAsync(false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updateInfo));

        var actionResult = await _controller.GetUpdates();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var results = okResult.Value as List<UpdateResource>;
        Assert.That(results, Is.Not.Null);

        var higherRelease = results.Find(r => r.Version == "99.0.0");
        Assert.That(higherRelease, Is.Not.Null);
        Assert.That(higherRelease.Installed, Is.False);

        var lowerRelease = results.Find(r => r.Version == "0.0.1");
        Assert.That(lowerRelease, Is.Not.Null);
        Assert.That(lowerRelease.Installed, Is.False);
    }

    [Test]
    public async Task GetUpdates_should_sort_prereleases_accurately()
    {
        var updateInfo = new UpdateInfo
        {
            CurrentVersion = BuildInfo.Version.ToString(),
            LatestVersion = "1.5.0",
            UpdateAvailable = true,
            Releases = new List<ReleaseInfo>
            {
                new() { Version = "1.4.0-beta.1", PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = "1.5.0-rc1", PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = "1.5.0", PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = "1.4.0", PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = "1.5.0-rc2", PublishedAt = DateTime.UtcNow, Body = "notes" },
                new() { Version = "1.4.0-beta.2", PublishedAt = DateTime.UtcNow, Body = "notes" },
            },
        };

        _updateService.CheckForUpdateAsync(false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updateInfo));

        var actionResult = await _controller.GetUpdates();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var results = okResult.Value as List<UpdateResource>;
        Assert.That(results, Is.Not.Null);
        Assert.That(results, Has.Count.EqualTo(6));

        var versions = results.ConvertAll(r => r.Version);
        Assert.That(versions, Is.EqualTo(new List<string>
        {
            "1.5.0",
            "1.5.0-rc2",
            "1.5.0-rc1",
            "1.4.0",
            "1.4.0-beta.2",
            "1.4.0-beta.1",
        }));
        Assert.That(results[0].Latest, Is.True);
        Assert.That(results[1].Latest, Is.False);
    }

    [Test]
    public void GetStatus_should_return_status_from_verification_service()
    {
        var state = new UpdateState
        {
            PreviousVersion = "1.0.0",
            TargetVersion = "1.1.0",
            State = UpdateLifecycleState.PendingVerification,
            InitiatedAt = DateTime.UtcNow,
            VerificationTimeoutSeconds = 90,
            BackupDirectory = "/path/to/backup",
            ErrorMessage = null,
        };

        _postUpdateVerificationService.GetUpdateState().Returns(state);

        var actionResult = _controller.GetStatus();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var status = okResult.Value as UpdateStatusResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.PreviousVersion, Is.EqualTo("1.0.0"));
        Assert.That(status.TargetVersion, Is.EqualTo("1.1.0"));
        Assert.That(status.State, Is.EqualTo(UpdateLifecycleState.PendingVerification));
        Assert.That(status.VerificationTimeoutSeconds, Is.EqualTo(90));
        Assert.That(status.BackupDirectory, Is.EqualTo("/path/to/backup"));
    }

    [Test]
    public void GetStatus_should_return_default_idle_status_when_verification_service_returns_idle()
    {
        _postUpdateVerificationService.GetUpdateState().Returns(new UpdateState
        {
            PreviousVersion = "1.0.0",
            State = UpdateLifecycleState.Idle,
        });

        var actionResult = _controller.GetStatus();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var status = okResult.Value as UpdateStatusResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.State, Is.EqualTo(UpdateLifecycleState.Idle));
    }

    [Test]
    public void GetStatus_should_return_default_idle_status_when_verification_service_is_null()
    {
        var controller = new UpdateController(_updateService, null);

        var actionResult = controller.GetStatus();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var status = okResult.Value as UpdateStatusResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.State, Is.EqualTo(UpdateLifecycleState.Idle));
    }

    [Test]
    public void GetStatus_should_return_rolled_back_status_with_error_message()
    {
        var state = new UpdateState
        {
            PreviousVersion = "1.0.0",
            TargetVersion = "1.1.0",
            State = UpdateLifecycleState.RolledBack,
            ErrorMessage = "Health check failed: DB corrupted",
        };

        _postUpdateVerificationService.GetUpdateState().Returns(state);

        var actionResult = _controller.GetStatus();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var status = okResult.Value as UpdateStatusResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.State, Is.EqualTo(UpdateLifecycleState.RolledBack));
        Assert.That(status.ErrorMessage, Is.EqualTo("Health check failed: DB corrupted"));
    }

    [Test]
    public async Task GetUpdates_should_populate_mechanism_and_package_info()
    {
        var packageProvider = new UpdatePackageProvider(platformOverride: "linux-x64", isDockerOverride: true);
        var controller = new UpdateController(_updateService, _postUpdateVerificationService, packageProvider);

        var updateInfo = new UpdateInfo
        {
            CurrentVersion = "1.0.0",
            LatestVersion = "2.0.0",
            UpdateAvailable = true,
            Releases = new List<ReleaseInfo>
            {
                new()
                {
                    Version = "2.0.0",
                    PublishedAt = DateTime.UtcNow,
                    Body = "notes",
                    Assets = new List<ReleaseAsset>
                    {
                        new("seedarr-linux-x64.tar.gz", "https://github.com/test/seedarr-linux-x64.tar.gz", 50000000),
                        new("seedarr-linux-x64.tar.gz.sha256", "https://github.com/test/seedarr-linux-x64.tar.gz.sha256", 64),
                    },
                },
            },
        };

        _updateService.CheckForUpdateAsync(false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updateInfo));

        var actionResult = await controller.GetUpdates();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var results = okResult.Value as List<UpdateResource>;
        Assert.That(results, Is.Not.Null);
        Assert.That(results[0].Mechanism, Is.EqualTo("Docker"));
        Assert.That(results[0].PackageUrl, Is.EqualTo("https://github.com/test/seedarr-linux-x64.tar.gz"));
        Assert.That(results[0].PackageFileName, Is.EqualTo("seedarr-linux-x64.tar.gz"));
        Assert.That(results[0].ReleaseChannel, Is.EqualTo("main"));
    }

    [Test]
    public void GetStatus_should_populate_mechanism_and_release_channel()
    {
        var packageProvider = new UpdatePackageProvider(isDockerOverride: true);
        var configService = Substitute.For<IConfigService>();
        configService.UpdateBranch.Returns("develop");

        var controller = new UpdateController(_updateService, _postUpdateVerificationService, packageProvider, configService);

        var actionResult = controller.GetStatus();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var status = okResult.Value as UpdateStatusResource;
        Assert.That(status, Is.Not.Null);
        Assert.That(status.Mechanism, Is.EqualTo("Docker"));
        Assert.That(status.ReleaseChannel, Is.EqualTo("develop"));
    }
}
