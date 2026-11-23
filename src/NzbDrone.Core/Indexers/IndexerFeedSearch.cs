using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using NLog;

namespace NzbDrone.Core.Indexers;

internal static class IndexerFeedSearch
{
    public static List<ReleaseInfo> Fetch(
        string indexerName,
        IndexerDefinition definition,
        SearchQuery searchQuery,
        CancellationToken cancellationToken,
        HttpClient client,
        Func<IndexerDefinition, SearchQuery, string> buildUrl,
        Func<string, IndexerDefinition, List<ReleaseInfo>> parse,
        IIndexerStatusService statusService,
        Logger logger)
    {
        var results = new List<ReleaseInfo>();
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url) || client == null)
        {
            return results;
        }

        searchQuery ??= new SearchQuery();

        try
        {
            var url = buildUrl(definition, searchQuery);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var response = client.SendAsync(request, cancellationToken).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                logger?.Warn("{0} search returned status code {1}", indexerName, response.StatusCode);
                var httpEx = new HttpRequestException(
                    $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                    null,
                    response.StatusCode);
                var retryAfter = ReadRetryAfter(response);
                if (retryAfter.HasValue)
                {
                    httpEx.Data["RetryAfter"] = retryAfter.Value;
                }

                if (definition.Id > 0 && statusService != null)
                {
                    statusService.RecordFailure(
                        definition.Id,
                        (int)response.StatusCode,
                        httpEx.Message,
                        httpEx,
                        retryAfter);
                    httpEx.Data["Recorded"] = true;
                }

                throw httpEx;
            }

            using var reader = new StreamReader(response.Content.ReadAsStream(cancellationToken));
            var xml = reader.ReadToEnd();
            return parse(xml, definition) ?? results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not HttpRequestException and not IndexerException)
        {
            logger?.Error(
                ex,
                "Failed to search {0} at {1} for query '{2}'",
                indexerName,
                definition.Url,
                searchQuery.Query);
        }

        return results;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta.HasValue)
        {
            return delta.Value;
        }

        var dated = response.Headers.RetryAfter?.Date;
        if (dated.HasValue)
        {
            var remaining = dated.Value - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        if (!response.Headers.TryGetValues("Retry-After", out var rawValues))
        {
            return null;
        }

        return IndexerStatusService.ParseRetryAfter(string.Join(",", rawValues));
    }
}
