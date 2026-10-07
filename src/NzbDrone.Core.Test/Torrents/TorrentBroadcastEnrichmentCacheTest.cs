using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentBroadcastEnrichmentCacheTest
{
    [Test]
    public void GetOrAddTrackers_returns_cached_value_until_invalidated()
    {
        var cache = new TorrentBroadcastEnrichmentCache();
        var loads = 0;

        List<TrackerEntry> Load()
        {
            loads++;
            return new List<TrackerEntry> { new() { TorrentId = 1, Url = "http://tracker/announce" } };
        }

        cache.GetOrAddTrackers(1, Load);
        cache.GetOrAddTrackers(1, Load);
        Assert.That(loads, Is.EqualTo(1));

        cache.Invalidate(1);
        cache.GetOrAddTrackers(1, Load);
        Assert.That(loads, Is.EqualTo(2));
    }
}
