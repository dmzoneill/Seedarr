using System;
using System.Collections.Generic;
using System.Text;

namespace NzbDrone.Core.WebSeeds;

public interface IWebSeedUrlResolver
{
    string ResolveMultiFileUrl(string baseUrl, IEnumerable<string> pathSegments);
    string ResolveMultiFileUrl(string baseUrl, string relativePath);
    string ResolveSingleFileUrl(string baseUrl, string torrentName);
}

public class WebSeedUrlResolver : IWebSeedUrlResolver
{
    public static string EncodePathSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return string.Empty;
        }

        var normalized = segment.Normalize(NormalizationForm.FormC);
        var escaped = Uri.EscapeDataString(normalized);

        return escaped
            .Replace("[", "%5B", StringComparison.Ordinal)
            .Replace("]", "%5D", StringComparison.Ordinal);
    }

    public string ResolveMultiFileUrl(string baseUrl, IEnumerable<string> pathSegments)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or whitespace.", nameof(baseUrl));
        }

        ArgumentNullException.ThrowIfNull(pathSegments);

        var trimmedBaseUrl = baseUrl.Trim();
        var normalizedBaseUrl = trimmedBaseUrl.EndsWith('/') ? trimmedBaseUrl : trimmedBaseUrl + "/";

        var encodedParts = new List<string>();

        foreach (var segment in pathSegments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            var parts = segment.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed == ".")
                {
                    continue;
                }

                if (trimmed == "..")
                {
                    throw new ArgumentException($"Path traversal attempt detected with '..' in path segment: '{segment}'", nameof(pathSegments));
                }

                encodedParts.Add(EncodePathSegment(trimmed));
            }
        }

        if (encodedParts.Count == 0)
        {
            return normalizedBaseUrl;
        }

        return normalizedBaseUrl + string.Join("/", encodedParts);
    }

    public string ResolveMultiFileUrl(string baseUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ResolveMultiFileUrl(baseUrl, Array.Empty<string>());
        }

        return ResolveMultiFileUrl(baseUrl, new[] { relativePath });
    }

    public string ResolveSingleFileUrl(string baseUrl, string torrentName)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or whitespace.", nameof(baseUrl));
        }

        var trimmedBaseUrl = baseUrl.Trim();

        if (trimmedBaseUrl.EndsWith('/'))
        {
            if (string.IsNullOrWhiteSpace(torrentName))
            {
                return trimmedBaseUrl;
            }

            var cleanName = torrentName.Trim().TrimStart('/', '\\');
            var parts = cleanName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (part.Trim() == "..")
                {
                    throw new ArgumentException($"Path traversal attempt detected in torrent name: '{torrentName}'", nameof(torrentName));
                }
            }

            return trimmedBaseUrl + EncodePathSegment(cleanName);
        }

        return trimmedBaseUrl;
    }
}
