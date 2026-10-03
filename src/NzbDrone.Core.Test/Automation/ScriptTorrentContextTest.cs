#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class ScriptTorrentContextTest
{
    private Torrent _torrent = null!;
    private AutomationExecutionResult _result = null!;
    private ScriptTorrentContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _torrent = new Torrent
        {
            Id = 1,
            Name = "Test.Movie.2024.1080p",
            TotalSize = 4_000_000_000,
            Category = "Movies",
        };
        _result = new AutomationExecutionResult();
        _context = new ScriptTorrentContext(_torrent, _result);
    }

    [Test]
    public void CleanFiles_should_match_comma_semicolon_and_space_separated_patterns()
    {
        _context.cleanFiles("tag1, tag2; tag3");

        Assert.That(_result.CleanFilePatterns, Has.Count.EqualTo(3));
        Assert.That(_result.CleanFilePatterns, Contains.Item("tag1"));
        Assert.That(_result.CleanFilePatterns, Contains.Item("tag2"));
        Assert.That(_result.CleanFilePatterns, Contains.Item("tag3"));
    }

    [TestCase("*.nfo, *.txt, *.sfv", new[] { "*.nfo", "*.txt", "*.sfv" })]
    [TestCase("*.exe;*.bat;*.cmd", new[] { "*.exe", "*.bat", "*.cmd" })]
    [TestCase("sample.mkv preview.mp4 proof.jpg", new[] { "sample.mkv", "preview.mp4", "proof.jpg" })]
    [TestCase("tag1, tag2; tag3 tag4", new[] { "tag1", "tag2", "tag3", "tag4" })]
    public void CleanFiles_should_split_by_all_supported_separators(string patterns, string[] expectedPatterns)
    {
        _context.cleanFiles(patterns);

        Assert.That(_result.CleanFilePatterns, Is.EquivalentTo(expectedPatterns));
    }

    [Test]
    public void CleanFiles_with_mixed_and_consecutive_delimiters_should_trim_and_ignore_empty()
    {
        _context.cleanFiles("  tag1 , ;   tag2 ;  tag3   ");

        Assert.That(_result.CleanFilePatterns, Has.Count.EqualTo(3));
        Assert.That(_result.CleanFilePatterns, Contains.Item("tag1"));
        Assert.That(_result.CleanFilePatterns, Contains.Item("tag2"));
        Assert.That(_result.CleanFilePatterns, Contains.Item("tag3"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t \n ")]
    public void CleanFiles_with_empty_or_whitespace_should_not_add_patterns(string? patterns)
    {
        _context.cleanFiles(patterns!);

        Assert.That(_result.CleanFilePatterns, Is.Empty);
    }

    [Test]
    public void CleanFiles_should_ignore_duplicate_patterns()
    {
        _context.cleanFiles("duplicate, duplicate; duplicate");

        Assert.That(_result.CleanFilePatterns, Has.Count.EqualTo(1));
        Assert.That(_result.CleanFilePatterns[0], Is.EqualTo("duplicate"));
    }
}
