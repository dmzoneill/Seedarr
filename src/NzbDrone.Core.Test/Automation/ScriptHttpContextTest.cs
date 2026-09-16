#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class ScriptHttpContextTest
{
    [Test]
    public void DefaultInsecureClient_should_be_reused_across_instances()
    {
        var context1 = new ScriptHttpContext();
        var context2 = new ScriptHttpContext();

        var client1 = context1.ResolveClient(allowInsecure: true);
        var client2 = context2.ResolveClient(allowInsecure: true);

        Assert.That(client1, Is.SameAs(client2));
        Assert.That(client1, Is.SameAs(ScriptHttpContext.DefaultInsecureClient));
    }

    [Test]
    public void DefaultSecureClient_should_be_reused_across_instances()
    {
        var context1 = new ScriptHttpContext();
        var context2 = new ScriptHttpContext();

        var client1 = context1.ResolveClient(allowInsecure: false);
        var client2 = context2.ResolveClient(allowInsecure: false);

        Assert.That(client1, Is.SameAs(client2));
        Assert.That(client1, Is.SameAs(ScriptHttpContext.DefaultSecureClient));
    }

    [Test]
    public void Secure_and_insecure_clients_should_be_distinct_with_infinite_timeout()
    {
        Assert.That(ScriptHttpContext.DefaultInsecureClient, Is.Not.SameAs(ScriptHttpContext.DefaultSecureClient));
        Assert.That(ScriptHttpContext.DefaultInsecureClient.Timeout, Is.EqualTo(Timeout.InfiniteTimeSpan));
        Assert.That(ScriptHttpContext.DefaultSecureClient.Timeout, Is.EqualTo(Timeout.InfiniteTimeSpan));
    }

    [Test]
    public void Custom_client_injection_should_override_default_clients()
    {
        using var customClient = new HttpClient();
        var context = new ScriptHttpContext(customClient);

        Assert.That(context.ResolveClient(allowInsecure: true), Is.SameAs(customClient));
        Assert.That(context.ResolveClient(allowInsecure: false), Is.SameAs(customClient));
    }

    [Test]
    public void SendAsync_should_pass_cancellation_token_to_client()
    {
        var handler = new TokenInspectingHandler();
        using var client = new HttpClient(handler);
        var context = new ScriptHttpContext(client);

        var options = new Dictionary<string, object> { ["timeoutSeconds"] = 5 };
        context.get("http://example.com/api/test", options);

        Assert.That(handler.ObservedToken.CanBeCanceled, Is.True);
    }

    [Test]
    public void SendAsync_should_throw_when_operation_is_canceled()
    {
        var handler = new CancelingHandler();
        using var client = new HttpClient(handler);
        var context = new ScriptHttpContext(client);

        var options = new Dictionary<string, object> { ["timeoutSeconds"] = 1 };

        Assert.Throws<TaskCanceledException>(() =>
        {
            context.get("http://example.com/api/test", options);
        });
    }

    [Test]
    public void Get_should_execute_request_and_parse_json_response()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"message\":\"hello\",\"value\":42}");
        using var client = new HttpClient(handler);
        var context = new ScriptHttpContext(client);

        var result = (Dictionary<string, object?>)context.get("http://example.com/api/test");

        Assert.That(result["status"], Is.EqualTo(200));
        Assert.That(result["ok"], Is.EqualTo(true));
        Assert.That(result["body"], Is.EqualTo("{\"message\":\"hello\",\"value\":42}"));
        Assert.That(result["json"], Is.Not.Null);
        var json = (Dictionary<string, object>)result["json"]!;
        Assert.That(json["message"].ToString(), Is.EqualTo("hello"));
    }

    [Test]
    public void Post_should_send_json_content()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Created, "{\"id\":1}");
        using var client = new HttpClient(handler);
        var context = new ScriptHttpContext(client);

        var body = new Dictionary<string, object> { ["name"] = "test" };
        var options = new Dictionary<string, object> { ["json"] = true };
        var result = (Dictionary<string, object?>)context.post("http://example.com/api/items", body, options);

        Assert.That(result["status"], Is.EqualTo(201));
        Assert.That(result["ok"], Is.EqualTo(true));
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
    }

    [Test]
    public void Put_should_send_form_content()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"updated\":true}");
        using var client = new HttpClient(handler);
        var context = new ScriptHttpContext(client);

        var body = new Dictionary<string, object> { ["status"] = "active" };
        var result = (Dictionary<string, object?>)context.put("http://example.com/api/items/1", body);

        Assert.That(result["status"], Is.EqualTo(200));
        Assert.That(result["ok"], Is.EqualTo(true));
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Put));
    }

    [Test]
    public void Delete_should_execute_delete_request()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NoContent, string.Empty);
        using var client = new HttpClient(handler);
        var context = new ScriptHttpContext(client);

        var result = (Dictionary<string, object?>)context.delete("http://example.com/api/items/1");

        Assert.That(result["status"], Is.EqualTo(204));
        Assert.That(result["ok"], Is.EqualTo(true));
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Delete));
    }

    private class TokenInspectingHandler : HttpMessageHandler
    {
        public CancellationToken ObservedToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private class CancelingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>();
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return tcs.Task;
        }
    }
}
