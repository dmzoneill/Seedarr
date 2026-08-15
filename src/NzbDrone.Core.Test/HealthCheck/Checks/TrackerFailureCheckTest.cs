using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class TrackerFailureCheckTest
    {
        private TrackerFailureCheck _subject;
        private ITorrentService _torrentService;

        [SetUp]
        public void SetUp()
        {
            _torrentService = Substitute.For<ITorrentService>();
            _subject = new TrackerFailureCheck(_torrentService);
        }

        [Test]
        public void Check_should_return_ok_when_no_errored_torrents()
        {
            _torrentService.GetAll().Returns(new List<Torrent>());

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        }

        [Test]
        public void Check_should_return_warning_when_errored_torrents_exist()
        {
            _torrentService.GetAll().Returns(new List<Torrent>
            {
                new Torrent { Name = "test", Status = TorrentStatus.Error }
            });

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        }

        [Test]
        public void Check_should_return_ok_when_torrents_have_non_error_status()
        {
            _torrentService.GetAll().Returns(new List<Torrent>
            {
                new Torrent { Name = "test", Status = TorrentStatus.Seeding }
            });

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        }
    }
}
