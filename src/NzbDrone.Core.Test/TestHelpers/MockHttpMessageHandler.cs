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
    private readonly object _syncRoot = new();
    private readonly Queue<HttpResponseMessage> _responses = new();

    public void Enqueue(HttpStatusCode statusCode, string content)
    {
        lock (_syncRoot)
        {
            _responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
        }
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

        lock (_syncRoot)
        {
            _responses.Enqueue(response);
        }
    }

    public void EnqueueBytes(HttpStatusCode statusCode, byte[] content)
    {
        lock (_syncRoot)
        {
            _responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(content),
            });
        }
    }

    public void EnqueueResponse(HttpResponseMessage response)
    {
        lock (_syncRoot)
        {
            _responses.Enqueue(response);
        }
    }

    public HttpRequestMessage LastRequest { get; private set; }
    public List<HttpRequestMessage> Requests { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PreserveContent(request);
        lock (_syncRoot)
        {
            LastRequest = request;
            Requests.Add(request);
            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }

            return Task.FromResult(GetNext());
        }
    }

    protected override HttpResponseMessage Send(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PreserveContent(request);
        lock (_syncRoot)
        {
            LastRequest = request;
            Requests.Add(request);
            if (ResponseFactory != null)
            {
                return ResponseFactory(request);
            }

            return GetNext();
        }
    }

    private static void PreserveContent(HttpRequestMessage request)
    {
        if (request?.Content != null && request.Content is not NonDisposingByteArrayContent)
        {
            var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var nonDisposing = new NonDisposingByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                nonDisposing.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Content = nonDisposing;
        }
    }

    private sealed class NonDisposingByteArrayContent : ByteArrayContent
    {
        public NonDisposingByteArrayContent(byte[] content)
            : base(content)
        {
        }

        protected override void Dispose(bool disposing)
        {
            // Do not dispose so that test assertions can read Content after the caller disposes HttpRequestMessage
        }
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
