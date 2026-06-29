using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class UpdateCheckTest
    {
        private UpdateCheck _subject;
        private IUpdateService _updateService;

        [SetUp]
        public void SetUp()
        {
            _updateService = Substitute.For<IUpdateService>();
            _subject = new UpdateCheck(_updateService);
        }

        [Test]
        public void Check_should_return_ok_when_no_update_available()
        {
            _updateService.CheckForUpdate().Returns(new UpdateInfo
            {
                CurrentVersion = "1.0.0",
                LatestVersion = "1.0.0",
                UpdateAvailable = false
            });

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        }

        [Test]
        public void Check_should_return_notice_when_update_available()
        {
            _updateService.CheckForUpdate().Returns(new UpdateInfo
            {
                CurrentVersion = "1.0.0",
                LatestVersion = "2.0.0",
                UpdateAvailable = true
            });

            var result = _subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        }
    }
}
