using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Telemetry;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Telemetry;

[TestFixture]
public class SystemResourceServiceTest
{
    private ITorrentService _torrentService;
    private IConfigService _configService;
    private IAppFolderInfo _appFolderInfo;
    private SystemResourceService _subject;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns("/tmp/seedarr_test_config");

        _subject = new SystemResourceService(_torrentService, _configService, _appFolderInfo);
    }

    [Test]
    public void GetHostMetrics_returns_valid_process_metrics()
    {
        var metrics = _subject.GetHostMetrics();

        Assert.That(metrics, Is.Not.Null);
        Assert.That(metrics.WorkingSetBytes, Is.GreaterThan(0));
        Assert.That(metrics.ThreadCount, Is.GreaterThan(0));
        Assert.That(metrics.CpuProcessPercent, Is.GreaterThanOrEqualTo(0.0).And.LessThanOrEqualTo(100.0));
        Assert.That(metrics.UptimeSeconds, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GetTorrentEngineMetrics_returns_metrics_from_torrent_service()
    {
        var torrents = new List<Torrent>
        {
            new() { Id = 1, Status = TorrentStatus.Seeding, UploadSpeed = 1000000, DownloadSpeed = 0 },
            new() { Id = 2, Status = TorrentStatus.Paused, UploadSpeed = 0, DownloadSpeed = 0 },
        };
        _torrentService.GetAll().Returns(torrents);

        var metrics = _subject.GetTorrentEngineMetrics();

        Assert.That(metrics, Is.Not.Null);
        Assert.That(metrics.EngineId, Is.EqualTo("SeedarrSimulationEngine"));
        Assert.That(metrics.IsRunning, Is.True);
        Assert.That(metrics.ActiveTorrents, Is.EqualTo(1));
    }

    [Test]
    public void GetSubsystemTelemetry_returns_populated_subsystem_summary()
    {
        var summary = _subject.GetSubsystemTelemetry();

        Assert.That(summary, Is.Not.Null);
        Assert.That(summary, Is.Not.Empty);
    }

    [Test]
    public async Task GetFullTelemetrySnapshotAsync_returns_complete_snapshot()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());

        var snapshot = await _subject.GetFullTelemetrySnapshotAsync(CancellationToken.None);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.Host, Is.Not.Null);
        Assert.That(snapshot.TorrentEngine, Is.Not.Null);
        Assert.That(snapshot.Subsystems, Is.Not.Null);
    }
}
