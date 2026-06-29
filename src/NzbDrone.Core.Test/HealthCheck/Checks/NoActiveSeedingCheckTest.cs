using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class NoActiveSeedingCheckTest
    {
        private NoActiveSeedingCheck _subject;
        private ITorrentService _torrentService;

        [SetUp]
        public void SetUp()
        {
            _torrentService = Substitute.For<ITorrentService>();
            _subject = new NoActiveSeedingCheck(_torrentService);
        }

        [Test]
        public void Check_should_return_ok_when_no_torrents()
        {
            _torrentService.GetAll().Returns(new List<Torrent>());

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        }

        [Test]
        public void Check_should_return_ok_when_seeding_exists()
        {
            _torrentService.GetAll().Returns(new List<Torrent>
            {
                new Torrent { Name = "test", Status = TorrentStatus.Seeding }
            });

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        }

        [Test]
        public void Check_should_return_notice_when_torrents_exist_but_none_seeding()
        {
            _torrentService.GetAll().Returns(new List<Torrent>
            {
                new Torrent { Name = "test", Status = TorrentStatus.Stopped }
            });

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        }
    }
}
