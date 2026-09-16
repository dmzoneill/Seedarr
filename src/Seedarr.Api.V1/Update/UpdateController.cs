using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;
using Seedarr.Http;

namespace Seedarr.Api.V1.Update;

[V1ApiController("update")]
public class UpdateController : Controller
{
    private readonly IUpdateService _updateService;

    public UpdateController(IUpdateService updateService = null)
    {
        _updateService = updateService ?? new UpdateService();
    }

    [HttpGet]
    public async Task<ActionResult<List<UpdateResource>>> GetUpdates(CancellationToken cancellationToken = default)
    {
        var info = await _updateService.CheckForUpdateAsync(false, cancellationToken).ConfigureAwait(false);
        var currentVersion = BuildInfo.Version?.ToString() ?? "1.0.0";
        var results = new List<UpdateResource>();
        var currentFound = false;

        var releases = info.Releases;
        if (releases == null || releases.Count == 0)
        {
            releases = UpdateService.LoadFromChangelog();
        }

        if (releases != null && releases.Count > 0)
        {
            var isFirst = true;
            foreach (var release in releases.OrderByDescending(r => SemVersion.TryParse(r.Version, out var v) ? v : new SemVersion(new Version(0, 0, 0))))
            {
                var isInstalled = string.Equals(release.Version, currentVersion, StringComparison.OrdinalIgnoreCase) ||
                    (Version.TryParse(release.Version?.TrimStart('v', 'V'), out var rv) && (AreVersionsEqual(rv, BuildInfo.Version) || (Version.TryParse(currentVersion?.TrimStart('v', 'V'), out var cv) && AreVersionsEqual(rv, cv)))) ||
                    (SemVersion.TryParse(release.Version, out var srv) && SemVersion.TryParse(currentVersion, out var scv) && srv == scv);

                if (isInstalled)
                {
                    currentFound = true;
                }

                var isLatest = isFirst;
                isFirst = false;

                var changes = ParseReleaseNotes(release.Body);

                results.Add(new UpdateResource
                {
                    Version = release.Version,
                    ReleaseDate = release.PublishedAt,
                    Installed = isInstalled,
                    Latest = isLatest,
                    Url = release.Url ?? $"https://github.com/dmzoneill/Seedarr/releases/tag/v{release.Version}",
                    Changes = changes,
                    IsContainerized = info.IsContainerized,
                });
            }
        }

        if (!currentFound)
        {
            if (results.Count == 0)
            {
                results.Add(new UpdateResource
                {
                    Version = currentVersion,
                    ReleaseDate = DateTime.UtcNow,
                    Installed = true,
                    Latest = true,
                    Url = "https://github.com/dmzoneill/Seedarr/releases",
                    Changes = new UpdateChanges
                    {
                        New = new List<string> { "Currently running version" },
                        Fixed = new List<string>(),
                    },
                    IsContainerized = info.IsContainerized,
                });
            }
            else
            {
                var matching = results.FirstOrDefault(r => string.Equals(r.Version, currentVersion, StringComparison.OrdinalIgnoreCase) ||
                    (Version.TryParse(r.Version?.TrimStart('v', 'V'), out var rv) && (AreVersionsEqual(rv, BuildInfo.Version) || (Version.TryParse(currentVersion?.TrimStart('v', 'V'), out var cv) && AreVersionsEqual(rv, cv)))) ||
                    (SemVersion.TryParse(r.Version, out var srv) && SemVersion.TryParse(currentVersion, out var scv) && srv == scv));
                if (matching != null)
                {
                    matching.Installed = true;
                }
            }
        }

        return Ok(results);
    }

    private static bool AreVersionsEqual(Version a, Version b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        return a.Major == b.Major &&
               a.Minor == b.Minor &&
               Math.Max(0, a.Build) == Math.Max(0, b.Build) &&
               Math.Max(0, a.Revision) == Math.Max(0, b.Revision);
    }

    private static UpdateChanges ParseReleaseNotes(string body)
    {
        var newItems = new List<string>();
        var fixedItems = new List<string>();

        if (string.IsNullOrWhiteSpace(body))
        {
            return new UpdateChanges { New = newItems, Fixed = fixedItems };
        }

        var currentSection = newItems;

        foreach (var rawLine in body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith('#'))
            {
                var heading = line.TrimStart('#', ' ').ToLowerInvariant();

                if (heading.Contains("fix") || heading.Contains("bug"))
                {
                    currentSection = fixedItems;
                }
                else
                {
                    currentSection = newItems;
                }

                continue;
            }

            if (line.StartsWith('*') || line.StartsWith('-'))
            {
                var item = line.TrimStart('*', '-', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(item) && !item.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase))
                {
                    var sanitized = SanitizeReleaseNote(item);
                    if (!string.IsNullOrWhiteSpace(sanitized))
                    {
                        currentSection.Add(sanitized);
                    }
                }
            }
        }

        return new UpdateChanges { New = newItems, Fixed = fixedItems };
    }

    private static string SanitizeReleaseNote(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return string.Empty;
        }

        // 1. Strip raw HTML tags (e.g. <details>, <summary>, <br>, etc.)
        item = Regex.Replace(item, @"<[^>]*>", string.Empty);

        // 2. Unescape / extract text from markdown links: [text](url) -> text
        item = Regex.Replace(item, @"\[([^\]]+)\]\([^)]+\)", "$1");

        // 3. Strip GitHub contributor mentions and PR URLs at the end of lines:
        // E.g., "by @octocat in https://github.com/owner/repo/pull/1" -> stripped
        item = Regex.Replace(item, @"\s*by\s+@[\w-]+(?:\s+in\s+https?://\S+)?", string.Empty, RegexOptions.IgnoreCase);

        // Strip standalone PR / commit links: "in https://github.com/..."
        item = Regex.Replace(item, @"\s*in\s+https?://\S+", string.Empty, RegexOptions.IgnoreCase);

        // Strip standalone bare links: "https://github.com/..."
        item = Regex.Replace(item, @"https?://\S+", string.Empty, RegexOptions.IgnoreCase);

        // Strip @mentions: (@user) or @user
        item = Regex.Replace(item, @"\(@[\w-]+\)", string.Empty);
        item = Regex.Replace(item, @"@[\w-]+", string.Empty);

        // Clean up redundant whitespace or trailing punctuation like dangling dashes/colons/commas
        item = Regex.Replace(item, @"\s+", " ").Trim();
        item = item.TrimEnd(' ', '-', ':', ',');

        return item;
    }
}
