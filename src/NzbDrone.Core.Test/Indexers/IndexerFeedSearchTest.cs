using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IndexerFeedSearchTest
{
    [Test]
    public void Fetch_records_retry_after_from_delta_and_date()
    {
        Assert.That(FetchOnce(HttpStatusCode.TooManyRequests, "120"), Is.EqualTo(TimeSpan.FromSeconds(120)));

        var dated = DateTimeOffset.UtcNow.AddMinutes(2).ToString("R");
        Assert.That(FetchOnce(HttpStatusCode.TooManyRequests, dated), Is.GreaterThan(TimeSpan.Zero));
        Assert.That(FetchOnce(HttpStatusCode.BadGateway, null), Is.Null);
    }

    [Test]
    public void Fetch_swallows_unexpected_builder_failures()
    {
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.OK, null));
        var results = IndexerFeedSearch.Fetch(
            "Torznab",
            new IndexerDefinition { Url = "http://indexer.example/api" },
            new SearchQuery(),
            CancellationToken.None,
            client,
            (_, _) => throw new InvalidOperationException("bad url"),
            (_, _) => new List<ReleaseInfo>(),
            null,
            LogManager.GetCurrentClassLogger());

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void Fetch_treats_an_http_timeout_as_a_failed_search_not_a_cancellation()
    {
        using var client = new HttpClient(new ThrowingHandler(new TaskCanceledException(
            "timeout",
            new TimeoutException())));

        var results = FetchWith(client, CancellationToken.None);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void Fetch_rethrows_when_the_caller_cancels()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = new HttpClient(new ThrowingHandler(new OperationCanceledException(cts.Token)));

        Assert.That(() => FetchWith(client, cts.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    private static List<ReleaseInfo> FetchWith(HttpClient client, CancellationToken cancellationToken)
    {
        return IndexerFeedSearch.Fetch(
            "Torznab",
            new IndexerDefinition { Url = "http://indexer.example/api" },
            new SearchQuery { Query = "linux" },
            cancellationToken,
            client,
            (_, _) => "http://indexer.example/api?t=search",
            (_, _) => new List<ReleaseInfo>(),
            null,
            LogManager.GetCurrentClassLogger());
    }

    private static TimeSpan? FetchOnce(HttpStatusCode status, string retryValue)
    {
        TimeSpan? captured = null;
        var statusService = Substitute.For<IIndexerStatusService>();
        statusService.When(s => s.RecordFailure(
            Arg.Any<int>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<TimeSpan?>()))
            .Do(call => captured = call.Arg<TimeSpan?>());

        using var client = new HttpClient(new StatusHandler(status, retryValue));
        Assert.Throws<HttpRequestException>(() => IndexerFeedSearch.Fetch(
            "Newznab",
            new IndexerDefinition { Id = 4, Url = "http://indexer.example/api", ApiKey = "key" },
            new SearchQuery { Query = "linux" },
            CancellationToken.None,
            client,
            (_, _) => "http://indexer.example/api?t=search",
            (_, _) => new List<ReleaseInfo>(),
            statusService,
            LogManager.GetCurrentClassLogger()));

        return captured;
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _retry;

        public StatusHandler(HttpStatusCode status, string retry)
        {
            _status = status;
            _retry = retry;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status);
            if (!string.IsNullOrEmpty(_retry))
            {
                response.Headers.TryAddWithoutValidation("Retry-After", _retry);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }
}
