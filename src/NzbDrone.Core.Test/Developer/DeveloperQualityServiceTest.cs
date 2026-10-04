// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Developer.Quality;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperQualityServiceTest
{
    [SetUp]
    public void SetUp()
    {
        DeveloperQualityService.ResetCache();
    }

    [Test]
    public async Task GetQualityReportAsync_when_apis_respond_parses_quality_gate_and_metrics()
    {
        var qualityGateJson = @"{
            ""projectStatus"": {
                ""status"": ""OK"",
                ""conditions"": [
                    { ""metricKey"": ""new_coverage"", ""status"": ""OK"", ""actualValue"": ""82.5"", ""errorThreshold"": ""80"" },
                    { ""metricKey"": ""new_duplicated_lines_density"", ""status"": ""OK"", ""actualValue"": ""2.1"", ""errorThreshold"": ""3"" }
                ]
            }
        }";

        var measuresJson = @"{
            ""component"": {
                ""measures"": [
                    { ""metric"": ""coverage"", ""value"": ""72.4"" },
                    { ""metric"": ""bugs"", ""value"": ""0"" },
                    { ""metric"": ""vulnerabilities"", ""value"": ""0"" },
                    { ""metric"": ""code_smells"", ""value"": ""0"" },
                    { ""metric"": ""security_hotspots_reviewed"", ""value"": ""100.0"" },
                    { ""metric"": ""reliability_rating"", ""value"": ""1.0"" },
                    { ""metric"": ""security_rating"", ""value"": ""1.0"" },
                    { ""metric"": ""sqale_rating"", ""value"": ""1.0"" }
                ]
            }
        }";

        var workflowsJson = @"{
            ""workflow_runs"": [
                {
                    ""name"": ""SonarCloud Analysis"",
                    ""status"": ""completed"",
                    ""conclusion"": ""success"",
                    ""run_number"": 105,
                    ""html_url"": ""https://github.com/dmzoneill/Seedarr/actions/runs/123"",
                    ""created_at"": ""2026-10-04T08:00:00Z""
                }
            ]
        }";

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, qualityGateJson);
        handler.Enqueue(HttpStatusCode.OK, measuresJson);
        handler.Enqueue(HttpStatusCode.OK, workflowsJson);

        var client = new HttpClient(handler);
        var service = new DeveloperQualityService(client);

        var report = await service.GetQualityReportAsync(CancellationToken.None);

        Assert.That(report, Is.Not.Null);
        Assert.That(report.QualityGate.Status, Is.EqualTo("OK"));
        Assert.That(report.Metrics.NewCodeCoverage, Is.EqualTo(82.5));
        Assert.That(report.Metrics.DuplicationDensity, Is.EqualTo(2.1));
        Assert.That(report.Metrics.Coverage, Is.EqualTo(72.4));
        Assert.That(report.Metrics.Bugs, Is.EqualTo(0));
        Assert.That(report.Metrics.ReliabilityRating, Is.EqualTo("A"));
        Assert.That(report.RecentPipelines, Has.Count.EqualTo(1));
        Assert.That(report.RecentPipelines[0].Name, Is.EqualTo("SonarCloud Analysis"));
    }

    [Test]
    public async Task GetQualityReportAsync_when_apis_fail_returns_fallback_report()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");

        var client = new HttpClient(handler);
        var service = new DeveloperQualityService(client);

        var report = await service.GetQualityReportAsync(CancellationToken.None);

        Assert.That(report, Is.Not.Null);
        Assert.That(report.QualityGate.Status, Is.EqualTo("OK"));
        Assert.That(report.Metrics.ReliabilityRating, Is.EqualTo("A"));
        Assert.That(report.RecentPipelines, Is.Not.Empty);
    }
}
