using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Mcp;
using Seedarr.Http;

namespace Seedarr.Api.V1.Mcp;

[V1ApiController("mcp")]
[Route("api/v1/mcp")]
public class McpController : ControllerBase
{
    private readonly IMcpService _mcpService;
    private readonly ISseSessionManager _sseSessionManager;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public McpController(IMcpService mcpService, ISseSessionManager sseSessionManager)
    {
        _mcpService = mcpService;
        _sseSessionManager = sseSessionManager;
    }

    [HttpGet("sse")]
    public async Task GetSse(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<string>();
        var sessionId = _sseSessionManager.CreateSession(channel);

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            var endpointMessage = $"event: endpoint\r\ndata: /api/v1/mcp/message?sessionId={sessionId}\r\n\r\n";
            await Response.WriteAsync(endpointMessage, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested &&
                   await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var message))
                {
                    var eventMessage = $"event: message\r\ndata: {message}\r\n\r\n";
                    await Response.WriteAsync(eventMessage, cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected normally
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "SSE connection error for session {0}", sessionId);
        }
        finally
        {
            _sseSessionManager.RemoveSession(sessionId);
        }
    }

    [HttpPost("message")]
    public async Task<IActionResult> PostMessage(
        [FromQuery] string sessionId = null,
        CancellationToken cancellationToken = default)
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            var parseError = JsonRpcResponse.CreateError(null, -32700, "Parse error: empty request body");
            return BadRequest(parseError);
        }

        var response = await _mcpService.ProcessMessageJsonAsync(body, cancellationToken);
        if (response == null)
        {
            return Accepted();
        }

        var responseJson = JsonSerializer.Serialize(response, McpJsonOptions.Default);

        var effectiveSessionId = !string.IsNullOrWhiteSpace(sessionId)
            ? sessionId
            : Request.Headers["x-mcp-session-id"].FirstOrDefault() ?? Request.Headers["Mcp-Session-Id"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(effectiveSessionId) && _sseSessionManager.TryGetSession(effectiveSessionId, out var channel))
        {
            channel.Writer.TryWrite(responseJson);
        }

        return Content(responseJson, "application/json");
    }
}
