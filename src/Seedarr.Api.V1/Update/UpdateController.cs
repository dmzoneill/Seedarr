using System;
using System.Collections.Generic;
using System.Linq;
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
            foreach (var release in releases.OrderByDescending(r => Version.TryParse(r.Version, out var v) ? v : new Version(0, 0, 0)))
            {
                var isInstalled = string.Equals(release.Version, currentVersion, StringComparison.OrdinalIgnoreCase) ||
                    (Version.TryParse(release.Version, out var rv) && rv == BuildInfo.Version);

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
                });
            }
            else
            {
                var matching = results.FirstOrDefault(r => string.Equals(r.Version, currentVersion, StringComparison.OrdinalIgnoreCase));
                if (matching != null)
                {
                    matching.Installed = true;
                }
            }
        }

        return Ok(results);
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
                    currentSection.Add(item);
                }
            }
        }

        return new UpdateChanges { New = newItems, Fixed = fixedItems };
    }
}
