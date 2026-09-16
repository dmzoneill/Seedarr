using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;
using Seedarr.Api.V1.Update;

namespace NzbDrone.Core.Test.Update;

[TestFixture]
public class UpdateControllerTest
{
    private IUpdateService _updateService;
    private UpdateController _controller;

    [SetUp]
    public void SetUp()
    {
        _updateService = Substitute.For<IUpdateService>();
        _controller = new UpdateController(_updateService);
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
}
