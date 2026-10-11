using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Core.Developer;
using NzbDrone.Core.Developer.Uml;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperInfrastructureStoresTest
{
    [Test]
    public void EventStore_ignores_null_and_supports_search_and_clear()
    {
        var store = new DeveloperEventStore();
        store.RecordEvent(null);
        store.RecordEvent(new DeveloperSyntheticEvent("Download", "payload-a"));
        store.RecordEvent(new DeveloperSyntheticEvent("Health", "payload-b"));

        Assert.That(store.GetRecentEvents(), Has.Count.EqualTo(2));
        Assert.That(store.GetRecentEvents(1), Has.Count.EqualTo(1));
        Assert.That(store.GetRecentEvents(50, "download"), Has.Count.EqualTo(1));

        store.Clear();
        Assert.That(store.GetRecentEvents(), Is.Empty);
    }

    [Test]
    public void HttpTrafficStore_redacts_sensitive_headers_and_truncates_body()
    {
        var store = new DeveloperHttpTrafficStore();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api");
        request.Headers.Add("Authorization", "secret");
        request.Headers.Add("X-Api-Key", "key123");
        request.Headers.Add("User-Agent", "Seedarr");

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Test", "1");

        var longBody = new string('x', 1200);
        store.Record(request, response, 12.3456, "TestEngine", longBody);

        var entry = store.GetRecent().Single();
        Assert.That(entry.RequestHeaders["Authorization"], Is.EqualTo("***REDACTED***"));
        Assert.That(entry.RequestHeaders["X-Api-Key"], Is.EqualTo("***REDACTED***"));
        Assert.That(entry.RequestHeaders["User-Agent"], Is.EqualTo("Seedarr"));
        Assert.That(entry.IsSuccess, Is.True);
        Assert.That(entry.TransportEngine, Is.EqualTo("TestEngine"));
        Assert.That(entry.ResponseBodyPreview, Does.EndWith("... [truncated]"));

        Assert.That(store.GetRecent(10, "example"), Has.Count.EqualTo(1));
        store.Clear();
        Assert.That(store.GetRecent(), Is.Empty);
    }

    [Test]
    public void HttpTrafficStore_records_error_when_response_missing()
    {
        var store = new DeveloperHttpTrafficStore();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://fail.local/hook");
        store.Record(request, null, 1, null, null, new InvalidOperationException("boom"));

        var entry = store.GetRecent().Single();
        Assert.That(entry.StatusCode, Is.EqualTo(500));
        Assert.That(entry.IsSuccess, Is.False);
        Assert.That(entry.ErrorMessage, Is.EqualTo("boom"));
        Assert.That(entry.ResponseBodyPreview, Is.EqualTo("boom"));
    }

    [Test]
    public void WebhookStore_records_and_limits_recent_entries()
    {
        var store = new DeveloperWebhookStore();
        store.Record("Radarr", "Grab", "127.0.0.1", "agent", 200, "{}", "ok", true);
        store.Record(null, null, null, null, 500, null, null, false);

        var recent = store.GetRecent();
        Assert.That(recent, Has.Count.EqualTo(2));
        Assert.That(recent[0].Success, Is.False);
        Assert.That(recent[1].EventType, Is.EqualTo("Grab"));

        store.Clear();
        Assert.That(store.GetRecent(), Is.Empty);
    }

    [Test]
    public void UmlService_generates_diagrams_for_each_type()
    {
        var service = new DeveloperUmlService();

        Assert.That(service.GetAvailableSubsystems(), Does.Contain("torrents"));
        Assert.That(service.GetAvailableDiagramTypes(), Does.Contain("class"));

        foreach (var diagramType in new[] { "class", "di", "state", "api", "frontend" })
        {
            var result = service.GenerateDiagram(new DeveloperUmlOptions
            {
                DiagramType = diagramType,
                Subsystem = "torrents",
                IncludeInterfaces = true,
                IncludeMethods = true,
            });

            Assert.That(result.MermaidCode, Is.Not.Empty);
            Assert.That(result.NodeCount, Is.GreaterThan(0));
        }
    }
}
