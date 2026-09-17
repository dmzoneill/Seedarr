using NUnit.Framework;
using NzbDrone.Core.ArrIntegration.Quality;

namespace NzbDrone.Core.Test.ArrIntegration.Quality;

[TestFixture]
public class QualityDefinitionServiceTest
{
    private QualityDefinitionService _subject;

    [SetUp]
    public void Setup()
    {
        _subject = new QualityDefinitionService();
    }

    [Test]
    public void IsWithinSizeBoundary_should_return_true_when_size_is_within_boundaries()
    {
        var definition = new QualityDefinition
        {
            QualityId = 1,
            Title = "WEBDL-1080p",
            MinSize = 5.0, // 5 MB / min
            MaxSize = 100.0, // 100 MB / min
            PreferredSize = 30.0,
        };

        // 2hr movie (120 min) -> min ~600 MB, max ~12 GB. Size = 4 GB.
        var sizeBytes = 4L * 1024 * 1024 * 1024;
        var result = _subject.IsWithinSizeBoundary(sizeBytes, 120, definition);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsWithinSizeBoundary_should_return_false_when_release_is_abnormally_small()
    {
        var definition = new QualityDefinition
        {
            QualityId = 1,
            Title = "WEBDL-1080p",
            MinSize = 5.0, // 5 MB / min -> 600 MB min for 2 hours
            MaxSize = 100.0,
        };

        // 10 MB for a 2-hour movie
        var sizeBytes = 10L * 1024 * 1024;
        var result = _subject.IsWithinSizeBoundary(sizeBytes, 120, definition);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsWithinSizeBoundary_should_return_false_when_release_is_abnormally_large()
    {
        var definition = new QualityDefinition
        {
            QualityId = 1,
            Title = "WEBDL-1080p",
            MinSize = 5.0,
            MaxSize = 100.0, // 100 MB / min -> ~12 GB max for 2 hours
        };

        // 50 GB capture for a 2-hour movie
        var sizeBytes = 50L * 1024 * 1024 * 1024;
        var result = _subject.IsWithinSizeBoundary(sizeBytes, 120, definition);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsWithinSizeBoundary_should_handle_unlimited_or_null_limits()
    {
        var definition = new QualityDefinition
        {
            QualityId = 2,
            Title = "Remux-2160p",
            MinSize = null,
            MaxSize = null,
        };

        var sizeBytes = 100L * 1024 * 1024 * 1024;
        var result = _subject.IsWithinSizeBoundary(sizeBytes, 120, definition);

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsWithinSizeBoundary_should_use_default_runtime_when_runtime_is_null_or_zero()
    {
        var definition = new QualityDefinition
        {
            QualityId = 1,
            Title = "WEBDL-1080p",
            MinSize = 5.0, // 5 MB / min -> defaults to 90 min = 450 MB
            MaxSize = 100.0,
        };

        // 10 MB with 0 runtime (defaults to 90 min) -> fails min size
        var smallResult = _subject.IsWithinSizeBoundary(10L * 1024 * 1024, 0, definition);
        Assert.That(smallResult, Is.False);

        // 2 GB with 0 runtime (defaults to 90 min) -> passes
        var validResult = _subject.IsWithinSizeBoundary(2L * 1024 * 1024 * 1024, 0, definition);
        Assert.That(validResult, Is.True);
    }

    [Test]
    public void IsWithinSizeBoundary_should_return_true_when_definition_is_null()
    {
        var result = _subject.IsWithinSizeBoundary(1024, 120, null);
        Assert.That(result, Is.True);
    }
}
