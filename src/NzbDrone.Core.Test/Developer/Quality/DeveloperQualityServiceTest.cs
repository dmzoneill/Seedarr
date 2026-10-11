using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Developer.Quality;

namespace NzbDrone.Core.Test.Developer.Quality;

[TestFixture]
public class DeveloperQualityServiceTest
{
    [SetUp]
    public void SetUp() => DeveloperQualityService.ResetCache();

    [Test]
    public async Task GetQualityReportAsync_uses_cache_on_second_call()
    {
        var handler = new QualityApiHandler();
        var service = new DeveloperQualityService(new HttpClient(handler));

        var first = await service.GetQualityReportAsync();
        var second = await service.GetQualityReportAsync();

        Assert.That(first.Metrics.NewCodeCoverage, Is.EqualTo(88.2));
        Assert.That(second, Is.SameAs(first));
        Assert.That(handler.CallCount, Is.EqualTo(3));
    }

    [Test]
    public async Task GetQualityReportAsync_falls_back_when_http_fails()
    {
        var handler = new QualityApiHandler(failAll: true);
        var service = new DeveloperQualityService(new HttpClient(handler));

        var report = await service.GetQualityReportAsync();

        Assert.That(report.QualityGate.Status, Is.EqualTo("OK"));
        Assert.That(report.Metrics.NewCodeCoverage, Is.EqualTo(82.0));
        Assert.That(report.RecentPipelines, Has.Count.GreaterThan(0));
    }

    [Test]
    public async Task GetQualityReportAsync_maps_sonar_rating_grades_and_technical_debt()
    {
        var handler = new QualityApiHandler(measuresJson: """
            {"component":{"measures":[
            {"metric":"reliability_rating","value":"2.0"},
            {"metric":"security_rating","value":"3.0"},
            {"metric":"sqale_rating","value":"4.5"},
            {"metric":"sqale_index","value":"90"}
            ]}}
            """);
        var service = new DeveloperQualityService(new HttpClient(handler));

        var report = await service.GetQualityReportAsync();

        Assert.That(report.Metrics.ReliabilityRating, Is.EqualTo("B"));
        Assert.That(report.Metrics.SecurityRating, Is.EqualTo("C"));
        Assert.That(report.Metrics.MaintainabilityRating, Is.EqualTo("E"));
        Assert.That(report.Metrics.TechnicalDebtFormatted, Is.EqualTo("90 min"));
    }

    [Test]
    public async Task GetQualityReportAsync_uses_fallback_pipelines_when_github_fails()
    {
        var handler = new QualityApiHandler(failGitHub: true);
        var service = new DeveloperQualityService(new HttpClient(handler));

        var report = await service.GetQualityReportAsync();

        Assert.That(report.RecentPipelines, Has.Count.GreaterThanOrEqualTo(4));
        Assert.That(report.RecentPipelines[0].Name, Does.Contain("SonarCloud"));
    }

    [Test]
    public void Default_constructor_builds_service()
    {
        var service = new DeveloperQualityService();
        Assert.That(service, Is.Not.Null);
    }

    private sealed class QualityApiHandler : HttpMessageHandler
    {
        private readonly bool _failAll;
        private readonly bool _failGitHub;
        private readonly string _measuresJson;
        private readonly string _workflowsJson;
        public int CallCount { get; private set; }

        public QualityApiHandler(bool failAll = false, bool failGitHub = false, string measuresJson = null, string workflowsJson = null)
        {
            _failAll = failAll;
            _failGitHub = failGitHub;
            _measuresJson = measuresJson;
            _workflowsJson = workflowsJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_failAll)
            {
                throw new HttpRequestException("simulated upstream failure");
            }

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("qualitygates", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""
                    {"projectStatus":{"status":"OK","conditions":[
                    {"metricKey":"new_coverage","status":"OK","actualValue":"88.2","errorThreshold":"80","comparator":"LT"},
                    {"metricKey":"new_duplicated_lines_density","status":"OK","actualValue":"1.1","errorThreshold":"3","comparator":"GT"}
                    ]}}
                    """));
            }

            if (path.Contains("measures", StringComparison.OrdinalIgnoreCase))
            {
                var measures = _measuresJson ?? """
                    {"component":{"measures":[
                    {"metric":"coverage","value":"70.5"},
                    {"metric":"bugs","value":"0"},
                    {"metric":"vulnerabilities","value":"0"},
                    {"metric":"code_smells","value":"2"},
                    {"metric":"security_hotspots_reviewed","value":"100"},
                    {"metric":"reliability_rating","value":"1.0"},
                    {"metric":"security_rating","value":"1.0"},
                    {"metric":"sqale_rating","value":"1.0"},
                    {"metric":"sqale_index","value":"42"}
                    ]}}
                    """;
                return Task.FromResult(JsonResponse(measures));
            }

            if (path.Contains("actions/runs", StringComparison.OrdinalIgnoreCase))
            {
                if (_failGitHub)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                var workflows = _workflowsJson ?? """
                    {"workflow_runs":[
                    {"name":"SonarCloud Analysis","status":"completed","conclusion":"success","run_number":99,"html_url":"https://example/run","created_at":"2026-10-11T18:00:00Z"}
                    ]}
                    """;
                return Task.FromResult(JsonResponse(workflows));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
