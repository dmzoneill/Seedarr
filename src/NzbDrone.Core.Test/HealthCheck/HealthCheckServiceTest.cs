using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;

namespace NzbDrone.Core.Test.HealthCheck
{
    [TestFixture]
    public class HealthCheckServiceTest
    {
        private HealthCheckService _subject;

        [Test]
        public void PerformChecks_should_return_empty_list_when_no_checks()
        {
            _subject = new HealthCheckService(new List<IHealthCheck>());

            var result = _subject.PerformChecks();

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void PerformChecks_should_return_results_from_all_checks()
        {
            var check1 = Substitute.For<IHealthCheck>();
            check1.Check().Returns(HealthCheckResult.Ok("Check1"));

            var check2 = Substitute.For<IHealthCheck>();
            check2.Check().Returns(HealthCheckResult.Warning("Check2", "Something is wrong"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check1, check2 });

            var result = _subject.PerformChecks();

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void PerformChecks_should_include_ok_results()
        {
            var check = Substitute.For<IHealthCheck>();
            check.Check().Returns(HealthCheckResult.Ok("OkCheck"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check });

            var result = _subject.PerformChecks();

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.First().Type, Is.EqualTo(HealthCheckResultType.Ok));
        }

        [Test]
        public void PerformChecks_should_include_warning_results()
        {
            var check = Substitute.For<IHealthCheck>();
            check.Check().Returns(HealthCheckResult.Warning("WarnCheck", "A warning occurred"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check });

            var result = _subject.PerformChecks();

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.First().Type, Is.EqualTo(HealthCheckResultType.Warning));
        }
    }
}
