using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers
{
    [TestFixture]
    public class RssSyncServiceTest
    {
        private RssSyncService _subject;

        [SetUp]
        public void SetUp()
        {
            _subject = new RssSyncService();
        }

        [Test]
        public void Null_seeder_handling_does_not_cause_false_rejection_when_allow_unknown_seeders_is_enabled()
        {
            var rule = new RssRule
            {
                Name = "Test Rule",
                IsEnabled = true,
                MinSeeders = 1,
                AllowUnknownSeeders = true
            };

            var release = new ReleaseInfo
            {
                Title = "Ubuntu.24.04.LTS",
                Seeders = null
            };

            Assert.That(_subject.MatchesRule(rule, release), Is.True);
            Assert.That(rule.Matches(release), Is.True);
        }

        [Test]
        public void Null_seeder_rejected_when_allow_unknown_seeders_is_disabled()
        {
            var rule = new RssRule
            {
                Name = "Test Rule",
                IsEnabled = true,
                MinSeeders = 1,
                AllowUnknownSeeders = false
            };

            var release = new ReleaseInfo
            {
                Title = "Ubuntu.24.04.LTS",
                Seeders = null
            };

            Assert.That(_subject.MatchesRule(rule, release), Is.False);
            Assert.That(rule.Matches(release), Is.False);
        }

        [TestCase(0, 1, false)]
        [TestCase(1, 1, true)]
        [TestCase(5, 1, true)]
        [TestCase(0, 0, true)]
        public void Known_seeders_evaluated_against_min_seeders(int releaseSeeders, int minSeeders, bool expected)
        {
            var rule = new RssRule
            {
                Name = "Test Rule",
                IsEnabled = true,
                MinSeeders = minSeeders,
                AllowUnknownSeeders = false
            };

            var release = new ReleaseInfo
            {
                Title = "Ubuntu.24.04.LTS",
                Seeders = releaseSeeders
            };

            Assert.That(_subject.MatchesRule(rule, release), Is.EqualTo(expected));
        }

        [Test]
        public void FilterReleases_should_include_null_seeder_releases_when_unknown_seeders_allowed()
        {
            var rule = new RssRule
            {
                Name = "Allow Unknown Rule",
                IsEnabled = true,
                MinSeeders = 2,
                AllowUnknownSeeders = true
            };

            var releases = new List<ReleaseInfo>
            {
                new() { Title = "Release.Unknown.Seeds", Seeders = null },
                new() { Title = "Release.Zero.Seeds", Seeders = 0 },
                new() { Title = "Release.Plenty.Seeds", Seeders = 10 }
            };

            var matched = _subject.FilterReleases(releases, new[] { rule });

            Assert.That(matched, Has.Count.EqualTo(2));
            Assert.That(matched[0].Title, Is.EqualTo("Release.Unknown.Seeds"));
            Assert.That(matched[1].Title, Is.EqualTo("Release.Plenty.Seeds"));
        }

        [Test]
        public void MatchesRule_should_respect_freeleech_and_size_criteria()
        {
            var rule = new RssRule
            {
                Name = "Freeleech Only",
                IsEnabled = true,
                FreeleechOnly = true,
                MinSizeBytes = 1000,
                MaxSizeBytes = 5000,
                MinSeeders = 1,
                AllowUnknownSeeders = true
            };

            var nonFreeleech = new ReleaseInfo
            {
                Title = "NonFreeleech",
                DownloadVolumeFactor = 1.0,
                Size = 2000,
                Seeders = null
            };

            var freeleech = new ReleaseInfo
            {
                Title = "Freeleech",
                DownloadVolumeFactor = 0.0,
                Size = 2000,
                Seeders = null
            };

            var freeleechTooLarge = new ReleaseInfo
            {
                Title = "Freeleech Large",
                DownloadVolumeFactor = 0.0,
                Size = 10000,
                Seeders = null
            };

            Assert.That(_subject.MatchesRule(rule, nonFreeleech), Is.False);
            Assert.That(_subject.MatchesRule(rule, freeleech), Is.True);
            Assert.That(_subject.MatchesRule(rule, freeleechTooLarge), Is.False);
        }

        [Test]
        public void GetFirstMatchingRule_should_return_highest_priority_rule_first()
        {
            var lowPriorityRule = new RssRule
            {
                Id = 1,
                Name = "Low Priority Rule",
                IsEnabled = true,
                Priority = 10,
                MustContain = "1080p"
            };

            var highPriorityRule = new RssRule
            {
                Id = 2,
                Name = "High Priority Rule",
                IsEnabled = true,
                Priority = 1,
                MustContain = "1080p"
            };

            var release = new ReleaseInfo
            {
                Title = "Test.Movie.2026.1080p"
            };

            var matched = _subject.GetFirstMatchingRule(new[] { lowPriorityRule, highPriorityRule }, release);

            Assert.That(matched, Is.Not.Null);
            Assert.That(matched.Id, Is.EqualTo(2));
            Assert.That(matched.Name, Is.EqualTo("High Priority Rule"));
        }

        [Test]
        public void MatchesRule_catastrophic_backtracking_regex_times_out_safely()
        {
            var rule = new RssRule
            {
                Name = "ReDoS Rule",
                IsEnabled = true,
                MustContain = "(a+)+$"
            };

            var release = new ReleaseInfo
            {
                Title = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!"
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = _subject.MatchesRule(rule, release);
            sw.Stop();

            Assert.That(result, Is.False);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000));
        }

        [Test]
        public void ShouldSyncIndexer_should_skip_indexer_during_ttl_cooldown_for_automated_sync()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var service = new RssSyncService(indexerStatusService: statusService);
            var indexer = new IndexerDefinition { Id = 1, Enable = true, EnableRss = true };

            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            statusService.IsDisabled(1).Returns(false);
            statusService.GetStatus(1).Returns(new IndexerStatus
            {
                IndexerId = 1,
                NextRssSyncTimeUtc = now.AddMinutes(15)
            });

            var result = service.ShouldSyncIndexer(indexer, isManual: false, now: now);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ShouldSyncIndexer_should_allow_indexer_during_ttl_cooldown_for_manual_sync()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var service = new RssSyncService(indexerStatusService: statusService);
            var indexer = new IndexerDefinition { Id = 1, Enable = true, EnableRss = true };

            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            statusService.IsDisabled(1).Returns(false);
            statusService.GetStatus(1).Returns(new IndexerStatus
            {
                IndexerId = 1,
                NextRssSyncTimeUtc = now.AddMinutes(15)
            });

            var result = service.ShouldSyncIndexer(indexer, isManual: true, now: now);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldSyncIndexer_should_allow_indexer_when_cooldown_elapsed()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var service = new RssSyncService(indexerStatusService: statusService);
            var indexer = new IndexerDefinition { Id = 1, Enable = true, EnableRss = true };

            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            statusService.IsDisabled(1).Returns(false);
            statusService.GetStatus(1).Returns(new IndexerStatus
            {
                IndexerId = 1,
                NextRssSyncTimeUtc = now.AddMinutes(-5)
            });

            var result = service.ShouldSyncIndexer(indexer, isManual: false, now: now);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldSyncIndexer_should_skip_disabled_rss_indexer()
        {
            var service = new RssSyncService();
            var indexer = new IndexerDefinition { Id = 1, Enable = true, EnableRss = false };

            Assert.That(service.ShouldSyncIndexer(indexer, isManual: false), Is.False);
            Assert.That(service.ShouldSyncIndexer(indexer, isManual: true), Is.False);
        }

        [Test]
        public void FilterEligibleIndexers_should_filter_out_indexers_in_cooldown_for_automated_sync()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var statusService = new IndexerStatusService(() => now);
            var service = new RssSyncService(indexerStatusService: statusService);

            var indexer1 = new IndexerDefinition { Id = 1, Enable = true, EnableRss = true };
            var indexer2 = new IndexerDefinition { Id = 2, Enable = true, EnableRss = true };

            // Indexer 1 synced recently with 30-min TTL
            statusService.RecordRssSync(1, 30);

            var eligibleAutomated = service.FilterEligibleIndexers(new[] { indexer1, indexer2 }, isManual: false, now: now);
            Assert.That(eligibleAutomated, Has.Count.EqualTo(1));
            Assert.That(eligibleAutomated[0].Id, Is.EqualTo(2));

            // Manual sync should include both
            var eligibleManual = service.FilterEligibleIndexers(new[] { indexer1, indexer2 }, isManual: true, now: now);
            Assert.That(eligibleManual, Has.Count.EqualTo(2));
        }
    }
}
