using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Torznab;

namespace NzbDrone.Core.Test.Indexers.Torznab
{
    [TestFixture]
    public class TorznabIndexerTest
    {
        private TorznabIndexer _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new TorznabIndexer();
        }

        [Test]
        public void Name_should_return_torznab()
        {
            Assert.That(_subject.Name, Is.EqualTo("Torznab"));
        }

        [Test]
        public void IndexerType_should_return_torznab()
        {
            Assert.That(_subject.IndexerType, Is.EqualTo("Torznab"));
        }

        [Test]
        public void TestConnection_should_return_false_when_url_is_invalid()
        {
            var definition = new IndexerDefinition
            {
                Url = "not-a-url",
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var result = _subject.TestConnection(definition);

            Assert.That(result, Is.False);
        }

        [Test]
        public void TestConnectionDetailed_should_return_error_when_url_is_null_or_empty()
        {
            var definition = new IndexerDefinition
            {
                Url = "",
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var result = _subject.TestConnectionDetailed(definition);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("URL is required"));
        }

        [Test]
        public void TestConnectionDetailed_should_return_error_when_connection_fails()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://127.0.0.1:59999",
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var result = _subject.TestConnectionDetailed(definition);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Unable to connect to Torznab"));
        }

        [Test]
        public void ParseResponse_should_parse_leechers_attribute()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Torznab Feed</title>
        <item>
            <title>Test.Release.2026.1080p</title>
            <link>http://indexer.local/torrent/123</link>
            <size>1073741824</size>
            <torznab:attr name=""seeders"" value=""42"" />
            <torznab:attr name=""leechers"" value=""15"" />
            <torznab:attr name=""infohash"" value=""0123456789abcdef0123456789abcdef01234567"" />
        </item>
    </channel>
</rss>";

            var definition = new IndexerDefinition { Id = 1, Name = "Torznab Test" };
            var results = _subject.ParseResponse(xml, definition);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Test.Release.2026.1080p"));
            Assert.That(results[0].Seeders, Is.EqualTo(42));
            Assert.That(results[0].Leechers, Is.EqualTo(15));
            Assert.That(results[0].InfoHash, Is.EqualTo("0123456789abcdef0123456789abcdef01234567"));
            Assert.That(results[0].IndexerId, Is.EqualTo(1));
            Assert.That(results[0].Indexer, Is.EqualTo("Torznab Test"));
        }

        [Test]
        public void ParseResponse_should_fallback_to_peers_attribute_when_leechers_not_present()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Release.Peers.Only</title>
            <torznab:attr name=""seeders"" value=""10"" />
            <torznab:attr name=""peers"" value=""7"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Seeders, Is.EqualTo(10));
            Assert.That(results[0].Leechers, Is.EqualTo(7));
        }

        [Test]
        public void ParseResponse_should_prefer_leechers_over_peers_regardless_of_order()
        {
            var xmlWithPeersFirst = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Peers.First</title>
            <torznab:attr name=""peers"" value=""25"" />
            <torznab:attr name=""leechers"" value=""5"" />
            <torznab:attr name=""seeders"" value=""20"" />
        </item>
    </channel>
</rss>";

            var results1 = _subject.ParseResponse(xmlWithPeersFirst);
            Assert.That(results1, Has.Count.EqualTo(1));
            Assert.That(results1[0].Seeders, Is.EqualTo(20));
            Assert.That(results1[0].Leechers, Is.EqualTo(5));

            var xmlWithLeechersFirst = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Leechers.First</title>
            <torznab:attr name=""leechers"" value=""5"" />
            <torznab:attr name=""peers"" value=""25"" />
            <torznab:attr name=""seeders"" value=""20"" />
        </item>
    </channel>
</rss>";

            var results2 = _subject.ParseResponse(xmlWithLeechersFirst);
            Assert.That(results2, Has.Count.EqualTo(1));
            Assert.That(results2[0].Seeders, Is.EqualTo(20));
            Assert.That(results2[0].Leechers, Is.EqualTo(5));
        }

        [Test]
        public void ParseResponse_should_parse_full_metadata_correctly()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Full.Metadata.Release</title>
            <enclosure url=""http://indexer.local/download/1.torrent"" length=""2048"" type=""application/x-bittorrent"" />
            <pubDate>Mon, 01 Jan 2026 12:00:00 +0000</pubDate>
            <torznab:attr name=""seeders"" value=""50"" />
            <torznab:attr name=""leechers"" value=""12"" />
            <torznab:attr name=""infohash"" value=""abcdef1234567890abcdef1234567890abcdef12"" />
            <torznab:attr name=""magneturl"" value=""magnet:?xt=urn:btih:abcdef1234567890abcdef1234567890abcdef12"" />
            <torznab:attr name=""category"" value=""2000"" />
            <torznab:attr name=""category"" value=""2040"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            var rel = results[0];
            Assert.That(rel.Title, Is.EqualTo("Full.Metadata.Release"));
            Assert.That(rel.DownloadUrl, Is.EqualTo("http://indexer.local/download/1.torrent"));
            Assert.That(rel.Size, Is.EqualTo(2048));
            Assert.That(rel.Seeders, Is.EqualTo(50));
            Assert.That(rel.Leechers, Is.EqualTo(12));
            Assert.That(rel.InfoHash, Is.EqualTo("abcdef1234567890abcdef1234567890abcdef12"));
            Assert.That(rel.MagnetUrl, Is.EqualTo("magnet:?xt=urn:btih:abcdef1234567890abcdef1234567890abcdef12"));
            Assert.That(rel.Categories, Is.EquivalentTo(new[] { "2000", "2040" }));
        }

        [Test]
        public void ParseResponse_should_return_empty_when_xml_is_empty_or_no_items()
        {
            Assert.That(_subject.ParseResponse(null), Is.Empty);
            Assert.That(_subject.ParseResponse(""), Is.Empty);
            Assert.That(_subject.ParseResponse("<rss><channel></channel></rss>"), Is.Empty);
        }
    }
}
