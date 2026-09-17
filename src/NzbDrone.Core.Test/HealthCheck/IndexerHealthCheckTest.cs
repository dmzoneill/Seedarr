using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.HealthCheck;

[TestFixture]
public class IndexerHealthCheckTest
{
    private IIndexerFactory _indexerFactory;
    private IIndexerStatusService _indexerStatusService;
    private IndexerHealthCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _indexerFactory = Substitute.For<IIndexerFactory>();
        _indexerStatusService = Substitute.For<IIndexerStatusService>();
        _subject = new IndexerHealthCheck(_indexerFactory, _indexerStatusService);
    }

    [Test]
    public void Check_should_return_ok_when_no_enabled_indexers()
    {
        _indexerFactory.All().Returns(new List<IndexerDefinition>());

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_ok_when_all_indexers_healthy()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Healthy Tracker", Enable = true };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexer });
        _indexerStatusService.IsDisabled(1).Returns(false);
        _indexerStatusService.IsAuthFailed(1).Returns(false);
        _indexerStatusService.IsRateLimited(1).Returns(false);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_error_when_indexer_auth_failed()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Bad Auth Tracker", Enable = true };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexer });
        _indexerStatusService.IsAuthFailed(1).Returns(true);
        _indexerStatusService.IsDisabled(1).Returns(true);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("Bad Auth Tracker"));
        Assert.That(result.Message, Does.Contain("Authentication failed"));
    }

    [Test]
    public void Check_should_return_warning_when_indexer_rate_limited()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Throttled Tracker", Enable = true };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexer });
        _indexerStatusService.IsRateLimited(1).Returns(true);
        _indexerStatusService.IsDisabled(1).Returns(true);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("Throttled Tracker"));
        Assert.That(result.Message, Does.Contain("rate limited"));
    }

    [Test]
    public void Check_should_return_warning_when_indexer_disabled_for_generic_errors()
    {
        var indexer = new IndexerDefinition { Id = 1, Name = "Failing Tracker", Enable = true };
        _indexerFactory.All().Returns(new List<IndexerDefinition> { indexer });
        _indexerStatusService.IsDisabled(1).Returns(true);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("Failing Tracker"));
        Assert.That(result.Message, Does.Contain("temporarily disabled"));
    }
}
