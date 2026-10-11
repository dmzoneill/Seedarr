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

    private sealed class QualityApiHandler : HttpMessageHandler
    {
        private readonly bool _failAll;
        public int CallCount { get; private set; }

        public QualityApiHandler(bool failAll = false) => _failAll = failAll;

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
                return Task.FromResult(JsonResponse("""
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
                    """));
            }

            if (path.Contains("actions/runs", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""
                    {"workflow_runs":[
                      {"name":"SonarCloud Analysis","status":"completed","conclusion":"success","run_number":99,"html_url":"https://example/run","created_at":"2026-10-11T18:00:00Z"}
                    ]}
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
