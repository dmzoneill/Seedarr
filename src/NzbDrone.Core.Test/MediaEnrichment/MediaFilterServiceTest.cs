using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;

namespace NzbDrone.Core.Test.MediaEnrichment;

[TestFixture]
public class MediaFilterServiceTest
{
    private MediaFilterService _service;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _service = new MediaFilterService();
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_filter_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup
            }
        }
    }

    [TestCase("sample.mkv", 20 * 1024 * 1024, true)]
    [TestCase("movie-sample.mp4", 45 * 1024 * 1024, true)]
    [TestCase("sample.avi", 69 * 1024 * 1024, true)]
    [TestCase("sample.mkv", 71 * 1024 * 1024, false)]
    [TestCase("Sampled.Beats.2024.mkv", 50 * 1024 * 1024, false)]
    [TestCase("The.Movie.2024.1080p.mkv", 2000L * 1024 * 1024, false)]
    public void IsSampleFile_evaluates_sample_name_and_size_threshold(string fileName, long size, bool expected)
    {
        var result = _service.IsSampleFile(fileName, size);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void IsSampleFile_detects_sample_directory_path()
    {
        var path = Path.Combine(_tempDir, "Sample", "clip.mkv");
        var result = _service.IsSampleFile(path, 15 * 1024 * 1024);
        Assert.That(result, Is.True);
    }

    [TestCase("movie.nfo", true)]
    [TestCase("checksum.sfv", true)]
    [TestCase("read_me.txt", true)]
    [TestCase("poster.jpg", true)]
    [TestCase("movie.mkv", false)]
    [TestCase("video.mp4", false)]
    public void IsClutterFile_detects_non_media_clutter(string fileName, bool expected)
    {
        var result = _service.IsClutterFile(fileName);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void SelectPrimaryMediaFile_chooses_main_movie_file_over_sample_and_clutter()
    {
        var movieFile = Path.Combine(_tempDir, "The.Matrix.1999.1080p.mkv");
        var sampleFile = Path.Combine(_tempDir, "sample.mkv");
        var nfoFile = Path.Combine(_tempDir, "movie.nfo");
        var jpgFile = Path.Combine(_tempDir, "cover.jpg");

        File.WriteAllBytes(movieFile, new byte[5000]);
        File.WriteAllBytes(sampleFile, new byte[500]);
        File.WriteAllBytes(nfoFile, new byte[100]);
        File.WriteAllBytes(jpgFile, new byte[200]);

        var selectedFromDir = _service.SelectPrimaryMediaFile(_tempDir);
        Assert.That(selectedFromDir, Is.EqualTo(movieFile));

        var selectedFromSample = _service.SelectPrimaryMediaFile(sampleFile);
        Assert.That(selectedFromSample, Is.EqualTo(movieFile));
    }

    [Test]
    public void FilterMediaFiles_excludes_clutter_and_prioritizes_non_samples()
    {
        var files = new[]
        {
            "/downloads/movie.nfo",
            "/downloads/sample.mkv",
            "/downloads/movie.sfv",
            "/downloads/Movie.1080p.mkv",
        };

        var filtered = _service.FilterMediaFiles(files).ToList();

        Assert.That(filtered, Does.Not.Contain("/downloads/movie.nfo"));
        Assert.That(filtered, Does.Not.Contain("/downloads/movie.sfv"));
        Assert.That(filtered.First(), Is.EqualTo("/downloads/Movie.1080p.mkv"));
    }
}
