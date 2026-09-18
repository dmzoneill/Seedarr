using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class TriggerEvaluatorTest
{
    [Test]
    public void BuildEvaluationContext_should_populate_progressPercent()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Progress = 0.75,
        };

        var context = TriggerEvaluator.BuildEvaluationContext(torrent);

        Assert.That(context.ContainsKey("torrent.progressPercent"), Is.True);
        Assert.That(context["torrent.progressPercent"], Is.EqualTo(75.0));
    }

    [TestCase("${torrent.progress} >= 100", 1.0, true)]
    [TestCase("${torrent.progress} >= 100", 0.99, false)]
    [TestCase("${torrent.progress} >= 50", 0.5, true)]
    [TestCase("${torrent.progress} >= 50", 0.25, false)]
    [TestCase("${torrent.progress} == 100", 1.0, true)]
    [TestCase("${torrent.progress} < 50", 0.25, true)]
    [TestCase("${torrent.progress} >= 0.5", 0.75, true)]
    [TestCase("${torrent.progressPercent} >= 50", 0.5, true)]
    public void EvaluateCondition_should_normalize_progress_comparisons(string condition, double progress, bool expected)
    {
        var torrent = new Torrent
        {
            Id = 1,
            Progress = progress,
        };

        var context = TriggerEvaluator.BuildEvaluationContext(torrent);
        var result = TriggerEvaluator.EvaluateCondition(condition, context);

        Assert.That(result, Is.EqualTo(expected));
    }
}
