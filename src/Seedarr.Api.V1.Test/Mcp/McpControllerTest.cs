using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Mcp;
using Seedarr.Api.V1.Mcp;

namespace Seedarr.Api.V1.Test.Mcp;

[TestFixture]
public class McpControllerTest
{
    private IMcpService _mcpService;
    private ISseSessionManager _sseSessionManager;
    private McpController _controller;

    [SetUp]
    public void SetUp()
    {
        _mcpService = Substitute.For<IMcpService>();
        _sseSessionManager = new SseSessionManager();
        _controller = new McpController(_mcpService, _sseSessionManager);
    }

    [Test]
    public async Task PostMessage_should_return_BadRequest_when_body_is_empty()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Array.Empty<byte>());
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await _controller.PostMessage();
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task PostMessage_should_return_ContentResult_with_response_json()
    {
        var jsonRequest = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";
        var expectedResponse = JsonRpcResponse.Success(1, new { });

        _mcpService.ProcessMessageJsonAsync(jsonRequest, Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonRequest));
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await _controller.PostMessage();
        Assert.That(result, Is.InstanceOf<ContentResult>());

        var content = result as ContentResult;
        Assert.That(content.ContentType, Is.EqualTo("application/json"));
        Assert.That(content.Content, Does.Contain("\"jsonrpc\":\"2.0\""));
    }

    [Test]
    public async Task PostMessage_should_broadcast_to_sse_channel_when_sessionId_matches()
    {
        var channel = Channel.CreateUnbounded<string>();
        var sessionId = _sseSessionManager.CreateSession(channel);

        var jsonRequest = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}";
        _mcpService.ProcessMessageJsonAsync(jsonRequest, Arg.Any<CancellationToken>())
            .Returns(JsonRpcResponse.Success(2, new { }));

        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonRequest));
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await _controller.PostMessage(sessionId);
        Assert.That(result, Is.InstanceOf<ContentResult>());

        Assert.That(channel.Reader.TryRead(out var streamedMessage), Is.True);
        Assert.That(streamedMessage, Does.Contain("\"jsonrpc\":\"2.0\""));
    }

    [Test]
    public async Task GetSse_should_send_initial_endpoint_event()
    {
        var context = new DefaultHttpContext();
        var stream = new MemoryStream();
        context.Response.Body = stream;
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            await _controller.GetSse(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation when CTS times out during SSE stream test
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var output = await reader.ReadToEndAsync();

        Assert.That(output, Does.Contain("event: endpoint"));
        Assert.That(output, Does.Contain("/api/v1/mcp/message?sessionId="));
    }
}
