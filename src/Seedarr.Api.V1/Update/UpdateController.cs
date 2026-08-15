using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;
using Seedarr.Http;

namespace Seedarr.Api.V1.Update;

[V1ApiController("update")]
public class UpdateController : Controller
{
    private readonly IUpdateService _updateService;

    public UpdateController(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    [HttpGet]
    public ActionResult<List<UpdateResource>> GetUpdates()
    {
        var info = _updateService.CheckForUpdate();
        var currentVersion = BuildInfo.Version.ToString();
        var results = new List<UpdateResource>();
        var currentFound = false;

        foreach (var release in info.Releases.OrderByDescending(r => Version.TryParse(r.Version, out var v) ? v : new Version(0, 0, 0)))
        {
            var isInstalled = string.Equals(release.Version, currentVersion, StringComparison.OrdinalIgnoreCase) ||
                              (Version.TryParse(release.Version, out var rv) && rv == BuildInfo.Version);

            if (isInstalled)
            {
                currentFound = true;
            }

            var isLatest = string.Equals(release.Version, info.LatestVersion, StringComparison.OrdinalIgnoreCase);

            var changes = ParseReleaseNotes(release.Body);

            results.Add(new UpdateResource
            {
                Version = release.Version,
                ReleaseDate = release.PublishedAt,
                Installed = isInstalled,
                Latest = isLatest,
                Changes = changes,
            });
        }

        if (!currentFound)
        {
            results.Add(new UpdateResource
            {
                Version = currentVersion,
                ReleaseDate = DateTime.UtcNow,
                Installed = true,
                Latest = !info.UpdateAvailable,
                Changes = new UpdateChanges
                {
                    New = new List<string> { "Currently running version" },
                    Fixed = new List<string>(),
                },
            });
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

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal) ||
                line.StartsWith("### ", StringComparison.Ordinal))
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

            if (line.StartsWith("* ", StringComparison.Ordinal) ||
                line.StartsWith("- ", StringComparison.Ordinal))
            {
                var item = line.Substring(2).Trim();
                if (!string.IsNullOrEmpty(item))
                {
                    currentSection.Add(item);
                }

                continue;
            }

            if (!string.IsNullOrEmpty(line) && !line.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase))
            {
                currentSection.Add(line);
            }
        }

        return new UpdateChanges { New = newItems, Fixed = fixedItems };
    }
}
