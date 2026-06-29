using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Test.TestHelpers;

/// <summary>
/// A test double for HttpMessageHandler that returns pre-enqueued responses.
/// Supports both SendAsync (used by download clients) and Send (used by Arr connections).
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public void Enqueue(HttpStatusCode statusCode, string content)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        });
    }

    public void EnqueueWithHeaders(
        HttpStatusCode statusCode,
        string content,
        Dictionary<string, string> headers)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

        foreach (var kvp in headers)
        {
            response.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        _responses.Enqueue(response);
    }

    public void EnqueueBytes(HttpStatusCode statusCode, byte[] content)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(content),
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(GetNext());
    }

    protected override HttpResponseMessage Send(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return GetNext();
    }

    private HttpResponseMessage GetNext()
    {
        if (_responses.TryDequeue(out var response))
        {
            return response;
        }

        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// A test double for HttpMessageHandler that always throws a specified exception.
/// Use this to exercise catch blocks in code that calls HttpClient.
/// </summary>
internal class ThrowingHttpMessageHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHttpMessageHandler(Exception exception)
    {
        _exception = exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromException<HttpResponseMessage>(_exception);
    }

    protected override HttpResponseMessage Send(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw _exception;
    }
}
