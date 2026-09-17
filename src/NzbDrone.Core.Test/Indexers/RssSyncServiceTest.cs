using System;
using System.Collections.Generic;
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
    }
}
