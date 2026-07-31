using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class NoTorrentsCheckTest
    {
        private NoTorrentsCheck _subject;
        private ITorrentService _torrentService;

        [SetUp]
        public void SetUp()
        {
            _torrentService = Substitute.For<ITorrentService>();
            _subject = new NoTorrentsCheck(_torrentService);
        }

        [Test]
        public void Check_should_return_warning_when_no_torrents()
        {
            _torrentService.GetAll().Returns(new List<Torrent>());

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        }

        [Test]
        public void Check_should_return_ok_when_torrents_exist()
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
