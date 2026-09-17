using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

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

        [Test]
        public void PerformChecks_should_publish_HealthIssueEvent_with_isResolved_false_when_check_fails()
        {
            var eventAggregator = Substitute.For<IEventAggregator>();
            var check = Substitute.For<IHealthCheck>();
            check.Check().Returns(HealthCheckResult.Error("DiskSpace", "Disk space is low"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

            _subject.PerformChecks();

            eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
                e.Source == "DiskSpace" &&
                e.Message == "Disk space is low" &&
                !e.IsResolved));
        }

        [Test]
        public void PerformChecks_should_publish_HealthIssueEvent_with_isResolved_true_when_failed_check_recovers()
        {
            var eventAggregator = Substitute.For<IEventAggregator>();
            var check = Substitute.For<IHealthCheck>();
            check.Check().Returns(HealthCheckResult.Warning("TrackerFailure", "Trackers failing"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

            _subject.PerformChecks();

            eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
                e.Source == "TrackerFailure" &&
                !e.IsResolved));

            eventAggregator.ClearReceivedCalls();

            check.Check().Returns(HealthCheckResult.Ok("TrackerFailure"));

            _subject.PerformChecks();

            eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
                e.Source == "TrackerFailure" &&
                e.IsResolved));
        }

        [Test]
        public void PerformChecks_should_not_publish_duplicate_events_for_consecutive_identical_checks()
        {
            var eventAggregator = Substitute.For<IEventAggregator>();
            var check = Substitute.For<IHealthCheck>();
            check.Check().Returns(HealthCheckResult.Error("DiskSpace", "Disk space is low"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

            _subject.PerformChecks();
            _subject.PerformChecks();
            _subject.PerformChecks();

            eventAggregator.Received(1).PublishEvent(Arg.Any<HealthIssueEvent>());

            eventAggregator.ClearReceivedCalls();

            check.Check().Returns(HealthCheckResult.Ok("DiskSpace"));

            _subject.PerformChecks();
            _subject.PerformChecks();
            _subject.PerformChecks();

            eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e => e.IsResolved));
        }

        [Test]
        public void PerformChecks_should_publish_HealthIssueEvent_when_check_throws_unhandled_exception()
        {
            var eventAggregator = Substitute.For<IEventAggregator>();
            var check = Substitute.For<IHealthCheck>();
            check.Check().Returns(_ => throw new InvalidOperationException("Check exploded"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

            _subject.PerformChecks();

            eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
                !e.IsResolved &&
                e.Message.Contains("Check exploded")));
        }

        [Test]
        public void PerformChecks_should_not_publish_event_when_consecutive_ok_checks_run()
        {
            var eventAggregator = Substitute.For<IEventAggregator>();
            var check = Substitute.For<IHealthCheck>();
            check.Check().Returns(HealthCheckResult.Ok("DatabaseCheck"));

            _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

            _subject.PerformChecks();
            _subject.PerformChecks();

            eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());
        }
    }
}
