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

    [TestCase("${torrent.category} != 'Movies'", "Movies", 1.5, false, false)]
    [TestCase("${torrent.category} != 'Movies'", "TV", 1.5, false, true)]
    [TestCase("${torrent.ratio} != 1.5", "Movies", 1.5, false, false)]
    [TestCase("${torrent.ratio} != 2.0", "Movies", 1.5, false, true)]
    [TestCase("${torrent.isPrivate} != false", "Movies", 1.5, false, false)]
    [TestCase("${torrent.isPrivate} != true", "Movies", 1.5, false, true)]
    [TestCase("${torrent.progress} != 100", "Movies", 1.5, false, true)]
    public void EvaluateCondition_should_evaluate_not_equals_operator(string condition, string category, double ratio, bool isPrivate, bool expected)
    {
        var torrent = new Torrent
        {
            Id = 1,
            Category = category,
            Ratio = ratio,
            IsPrivate = isPrivate,
            Progress = 0.5,
        };

        var context = TriggerEvaluator.BuildEvaluationContext(torrent);
        var result = TriggerEvaluator.EvaluateCondition(condition, context);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("${torrent.ratio} <= 2.0", 1.5, true)]
    [TestCase("${torrent.ratio} <= 1.5", 1.5, true)]
    [TestCase("${torrent.ratio} <= 1.0", 1.5, false)]
    [TestCase("${torrent.progress} <= 50", 0.5, true)]
    [TestCase("${torrent.progress} <= 50", 0.25, true)]
    [TestCase("${torrent.progress} <= 50", 0.75, false)]
    [TestCase("${torrent.progress} <= 100", 1.0, true)]
    public void EvaluateCondition_should_evaluate_less_than_or_equal_operator(string condition, double value, bool expected)
    {
        var torrent = new Torrent
        {
            Id = 1,
            Ratio = value,
            Progress = value,
        };

        var context = TriggerEvaluator.BuildEvaluationContext(torrent);
        var result = TriggerEvaluator.EvaluateCondition(condition, context);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("${torrent.ratio} > 1.0", 1.5, true)]
    [TestCase("${torrent.ratio} > 1.5", 1.5, false)]
    [TestCase("${torrent.ratio} > 2.0", 1.5, false)]
    [TestCase("${torrent.progress} > 50", 0.75, true)]
    [TestCase("${torrent.progress} > 50", 0.5, false)]
    [TestCase("${torrent.progress} > 50", 0.25, false)]
    [TestCase("${torrent.progress} > 100", 1.0, false)]
    public void EvaluateCondition_should_evaluate_greater_than_operator(string condition, double value, bool expected)
    {
        var torrent = new Torrent
        {
            Id = 1,
            Ratio = value,
            Progress = value,
        };

        var context = TriggerEvaluator.BuildEvaluationContext(torrent);
        var result = TriggerEvaluator.EvaluateCondition(condition, context);

        Assert.That(result, Is.EqualTo(expected));
    }
}
