using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;

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

        [Test]
        public void FilterNewReleases_should_return_all_releases_when_repository_is_null()
        {
            var service = new RssSyncService();
            var releases = new List<ReleaseInfo>
            {
                new() { Guid = "g1", Title = "Movie 1" },
                new() { Guid = "g2", Title = "Movie 2" }
            };

            var result = service.FilterNewReleases(1, releases);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void FilterNewReleases_should_filter_out_releases_matching_seen_guids()
        {
            var repo = Substitute.For<IRssSeenReleaseRepository>();
            repo.GetSeenGuids(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "seen-guid" });
            repo.GetSeenInfoHashes(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var service = new RssSyncService(seenReleaseRepository: repo);
            var releases = new List<ReleaseInfo>
            {
                new() { Guid = "seen-guid", Title = "Old Release" },
                new() { Guid = "new-guid", Title = "New Release" }
            };

            var result = service.FilterNewReleases(1, releases);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Guid, Is.EqualTo("new-guid"));
        }

        [Test]
        public void FilterNewReleases_should_filter_out_releases_matching_seen_infohashes_case_insensitively()
        {
            var repo = Substitute.For<IRssSeenReleaseRepository>();
            repo.GetSeenGuids(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            repo.GetSeenInfoHashes(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "0123456789abcdef0123456789abcdef01234567" });

            var service = new RssSyncService(seenReleaseRepository: repo);
            var releases = new List<ReleaseInfo>
            {
                new() { Guid = "guid-1", InfoHash = "0123456789ABCDEF0123456789ABCDEF01234567", Title = "Old Hash" },
                new() { Guid = "guid-2", InfoHash = "fedcba9876543210fedcba9876543210fedcba98", Title = "New Hash" }
            };

            var result = service.FilterNewReleases(1, releases);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Guid, Is.EqualTo("guid-2"));
        }

        [Test]
        public void FilterNewReleases_should_deduplicate_items_within_same_batch()
        {
            var repo = Substitute.For<IRssSeenReleaseRepository>();
            repo.GetSeenGuids(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            repo.GetSeenInfoHashes(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var service = new RssSyncService(seenReleaseRepository: repo);
            var releases = new List<ReleaseInfo>
            {
                new() { Guid = "dup-guid", Title = "Release First" },
                new() { Guid = "dup-guid", Title = "Release Duplicate" },
                new() { Guid = "unique-1", InfoHash = "hash1", Title = "Release 1" },
                new() { Guid = "unique-2", InfoHash = "hash1", Title = "Release 2 Duplicate Hash" }
            };

            var result = service.FilterNewReleases(1, releases);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].Title, Is.EqualTo("Release First"));
            Assert.That(result[1].Title, Is.EqualTo("Release 1"));
        }

        [Test]
        public void FilterReleases_with_indexerId_should_filter_new_releases_before_evaluating_rules()
        {
            var repo = Substitute.For<IRssSeenReleaseRepository>();
            repo.GetSeenGuids(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "seen-guid" });
            repo.GetSeenInfoHashes(1).Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var service = new RssSyncService(seenReleaseRepository: repo);
            var rule = new RssRule
            {
                Name = "Match All",
                IsEnabled = true,
                AllowUnknownSeeders = true
            };

            var releases = new List<ReleaseInfo>
            {
                new() { Guid = "seen-guid", Title = "Seen Movie" },
                new() { Guid = "new-guid", Title = "New Movie" }
            };

            var matched = service.FilterReleases(1, releases, new[] { rule });

            Assert.That(matched, Has.Count.EqualTo(1));
            Assert.That(matched[0].Guid, Is.EqualTo("new-guid"));
        }

        [Test]
        public void RecordSeen_and_PurgeSeenReleases_should_delegate_to_seen_repository()
        {
            var repo = Substitute.For<IRssSeenReleaseRepository>();
            var service = new RssSyncService(seenReleaseRepository: repo);
            var release = new ReleaseInfo { Guid = "g1" };

            service.RecordSeen(1, release, RssSeenStatus.Grabbed, 10);
            repo.Received(1).MarkSeen(1, release, RssSeenStatus.Grabbed, 10);

            var batch = new List<ReleaseInfo> { release };
            service.RecordSeenBatch(1, batch, RssSeenStatus.Ignored);
            repo.Received(1).MarkSeenBatch(1, batch, RssSeenStatus.Ignored);

            repo.PurgeOlderThan(TimeSpan.FromDays(14)).Returns(5);
            var purged = service.PurgeSeenReleases(TimeSpan.FromDays(14));
            Assert.That(purged, Is.EqualTo(5));
        }

        [Test]
        public void RuleMatching_evaluates_regexes_size_seeders_freeleech_age_and_category_accurately()
        {
            var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
            var rule = new RssRule
            {
                Name = "Complex Rule",
                IsEnabled = true,
                MustContain = @"\b(2160p|4K)\b",
                MustNotContain = @"\b(CAM|TS)\b",
                MinSizeBytes = 1000,
                MaxSizeBytes = 5000,
                MinSeeders = 5,
                AllowUnknownSeeders = false,
                FreeleechOnly = true,
                MaxAgeDays = 7,
                CategoryId = 2000
            };

            var validRelease = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.UHD",
                Size = 3000,
                Seeders = 10,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "2000" }
            };

            Assert.That(_subject.MatchesRule(rule, validRelease, now), Is.True);

            // MustContain failed
            var missingContain = new ReleaseInfo
            {
                Title = "The.Matrix.1999.1080p.HD",
                Size = 3000,
                Seeders = 10,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "2000" }
            };
            Assert.That(_subject.MatchesRule(rule, missingContain, now), Is.False);

            // MustNotContain failed
            var hasForbidden = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.CAM",
                Size = 3000,
                Seeders = 10,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "2000" }
            };
            Assert.That(_subject.MatchesRule(rule, hasForbidden, now), Is.False);

            // Size below min
            var tooSmall = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.UHD",
                Size = 500,
                Seeders = 10,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "2000" }
            };
            Assert.That(_subject.MatchesRule(rule, tooSmall, now), Is.False);

            // Size above max
            var tooLarge = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.UHD",
                Size = 6000,
                Seeders = 10,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "2000" }
            };
            Assert.That(_subject.MatchesRule(rule, tooLarge, now), Is.False);

            // Seeders below min
            var lowSeeds = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.UHD",
                Size = 3000,
                Seeders = 2,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "2000" }
            };
            Assert.That(_subject.MatchesRule(rule, lowSeeds, now), Is.False);

            // Freeleech only but factor > 0
            var notFreeleech = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.UHD",
                Size = 3000,
                Seeders = 10,
                DownloadVolumeFactor = 1.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "2000" }
            };
            Assert.That(_subject.MatchesRule(rule, notFreeleech, now), Is.False);

            // Age older than max
            var tooOld = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.UHD",
                Size = 3000,
                Seeders = 10,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-10),
                Categories = new List<string> { "2000" }
            };
            Assert.That(_subject.MatchesRule(rule, tooOld, now), Is.False);

            // Category mismatch
            var wrongCategory = new ReleaseInfo
            {
                Title = "The.Matrix.1999.2160p.UHD",
                Size = 3000,
                Seeders = 10,
                DownloadVolumeFactor = 0.0,
                PublishDate = now.AddDays(-2),
                Categories = new List<string> { "5000" }
            };
            Assert.That(_subject.MatchesRule(rule, wrongCategory, now), Is.False);
        }

        [Test]
        public void Sync_coordinates_fetching_across_enabled_indexers_and_grabs_matching_releases()
        {
            var indexerRepo = Substitute.For<IIndexerRepository>();
            var ruleRepo = Substitute.For<IRssRuleRepository>();
            var torrentService = Substitute.For<ITorrentService>();
            var grabRepo = Substitute.For<IRssGrabHistoryRepository>();
            var downloadHistoryRepo = Substitute.For<IDownloadHistoryRepository>();
            var mockIndexer = Substitute.For<IIndexer>();

            var enabledIndexer = new IndexerDefinition { Id = 1, Name = "Indexer 1", Enable = true, EnableRss = true };
            var disabledRssIndexer = new IndexerDefinition { Id = 2, Name = "Indexer 2", Enable = true, EnableRss = false };

            indexerRepo.All().Returns(new List<IndexerDefinition> { enabledIndexer, disabledRssIndexer });

            var rule = new RssRule
            {
                Id = 1,
                Name = "Rule 1",
                IsEnabled = true,
                MustContain = "MatchMe",
                MinSeeders = 1,
                AllowUnknownSeeders = true
            };
            ruleRepo.All().Returns(new List<RssRule> { rule });

            var releaseMatching = new ReleaseInfo
            {
                Title = "Release.MatchMe.1080p",
                InfoHash = "hash111111111111111111111111111111111111",
                Size = 1000,
                Seeders = 5,
                IndexerId = 1
            };
            var releaseNonMatching = new ReleaseInfo
            {
                Title = "Release.Other.1080p",
                InfoHash = "hash222222222222222222222222222222222222",
                Size = 1000,
                Seeders = 5,
                IndexerId = 1
            };

            mockIndexer.Search(enabledIndexer, Arg.Any<SearchQuery>()).Returns(new List<ReleaseInfo> { releaseMatching, releaseNonMatching });
            torrentService.ExistsByInfoHash(Arg.Any<string>()).Returns(false);
            torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var service = new RssSyncService(
                torrentService: torrentService,
                grabHistoryRepository: grabRepo,
                indexerRepository: indexerRepo,
                rssRuleRepository: ruleRepo,
                downloadHistoryRepository: downloadHistoryRepo,
                indexerInstanceFactory: _ => mockIndexer);

            var grabbedCount = service.Sync();

            Assert.That(grabbedCount, Is.EqualTo(1));
            torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.InfoHash == "hash111111111111111111111111111111111111"));
            downloadHistoryRepo.Received(1).Insert(Arg.Is<DownloadHistory>(dh => dh.InfoHash == "hash111111111111111111111111111111111111"));
            mockIndexer.DidNotReceive().Search(disabledRssIndexer, Arg.Any<SearchQuery>());
        }

        [Test]
        public void Sync_deduplicates_against_active_torrents_and_download_history()
        {
            var indexerRepo = Substitute.For<IIndexerRepository>();
            var ruleRepo = Substitute.For<IRssRuleRepository>();
            var torrentService = Substitute.For<ITorrentService>();
            var downloadHistoryRepo = Substitute.For<IDownloadHistoryRepository>();
            var mockIndexer = Substitute.For<IIndexer>();

            var indexerDef = new IndexerDefinition { Id = 1, Name = "Indexer 1", Enable = true, EnableRss = true };
            indexerRepo.All().Returns(new List<IndexerDefinition> { indexerDef });

            var rule = new RssRule { Id = 1, Name = "Grab All", IsEnabled = true, AllowUnknownSeeders = true };
            ruleRepo.All().Returns(new List<RssRule> { rule });

            var relActive = new ReleaseInfo { Title = "Rel.Active", InfoHash = "hash-active", Size = 100, Seeders = 5 };
            var relHistory = new ReleaseInfo { Title = "Rel.History", InfoHash = "hash-history", Size = 100, Seeders = 5 };
            var relNew = new ReleaseInfo { Title = "Rel.New", InfoHash = "hash-new", Size = 100, Seeders = 5 };

            mockIndexer.Search(indexerDef, Arg.Any<SearchQuery>()).Returns(new List<ReleaseInfo> { relActive, relHistory, relNew });

            // Active in torrent service
            torrentService.ExistsByInfoHash("hash-active").Returns(true);
            torrentService.ExistsByInfoHash("hash-history").Returns(false);
            torrentService.ExistsByInfoHash("hash-new").Returns(false);

            // In download history
            downloadHistoryRepo.FindByInfoHash("hash-history").Returns(new DownloadHistory { InfoHash = "hash-history" });
            downloadHistoryRepo.FindByInfoHash("hash-new").Returns((DownloadHistory)null);

            torrentService.Add(Arg.Any<Torrent>()).Returns(callInfo => callInfo.Arg<Torrent>());

            var service = new RssSyncService(
                torrentService: torrentService,
                indexerRepository: indexerRepo,
                rssRuleRepository: ruleRepo,
                downloadHistoryRepository: downloadHistoryRepo,
                indexerInstanceFactory: _ => mockIndexer);

            var grabbedCount = service.Sync();

            Assert.That(grabbedCount, Is.EqualTo(1));
            torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.InfoHash == "hash-new"));
            torrentService.DidNotReceive().Add(Arg.Is<Torrent>(t => t.InfoHash == "hash-active"));
            torrentService.DidNotReceive().Add(Arg.Is<Torrent>(t => t.InfoHash == "hash-history"));
        }
    }
}
