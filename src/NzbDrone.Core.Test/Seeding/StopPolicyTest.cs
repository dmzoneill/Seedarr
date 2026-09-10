using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class StopPolicyTest
{
    private IConfigService _configService;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
    }

    [Test]
    public void SelectStoppedTorrents_returns_empty_when_max_percentage_zero()
    {
        _configService.UploadStoppedMinPercentage.Returns(0);
        _configService.UploadStoppedMaxPercentage.Returns(0);

        var subject = new StopPolicy(_configService, new RandomNumberGenerator(42));
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1 },
            new Torrent { Id = 2 }
        };

        var stopped = subject.SelectStoppedTorrents(torrents);

        Assert.That(stopped, Is.Empty);
    }

    [Test]
    public void SelectStoppedTorrents_never_stops_force_start_torrents()
    {
        _configService.UploadStoppedMinPercentage.Returns(100);
        _configService.UploadStoppedMaxPercentage.Returns(100);

        var subject = new StopPolicy(_configService, new RandomNumberGenerator(42));
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, ForceStart = true },
            new Torrent { Id = 2, ForceStart = true }
        };

        var stopped = subject.SelectStoppedTorrents(torrents);

        Assert.That(stopped, Is.Empty);
    }

    [Test]
    public void SelectStoppedTorrents_leaves_at_least_one_eligible_active()
    {
        _configService.UploadStoppedMinPercentage.Returns(100);
        _configService.UploadStoppedMaxPercentage.Returns(100);

        var subject = new StopPolicy(_configService, new RandomNumberGenerator(42));
        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1 },
            new Torrent { Id = 2 },
            new Torrent { Id = 3 }
        };

        var stopped = subject.SelectStoppedTorrents(torrents);

        // stoppedCount = Math.Min(3, 3 - 1) = 2
        Assert.That(stopped.Count, Is.EqualTo(2));
    }

    [Test]
    public void SelectStoppedTorrents_is_deterministic_under_fixed_random()
    {
        _configService.UploadStoppedMinPercentage.Returns(50);
        _configService.UploadStoppedMaxPercentage.Returns(50);

        var subject1 = new StopPolicy(_configService, new RandomNumberGenerator(12345));
        var subject2 = new StopPolicy(_configService, new RandomNumberGenerator(12345));

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1 },
            new Torrent { Id = 2 },
            new Torrent { Id = 3 },
            new Torrent { Id = 4 },
            new Torrent { Id = 5 }
        };

        var stopped1 = subject1.SelectStoppedTorrents(torrents);
        var stopped2 = subject2.SelectStoppedTorrents(torrents);

        Assert.That(stopped1, Is.EquivalentTo(stopped2));
    }
}
