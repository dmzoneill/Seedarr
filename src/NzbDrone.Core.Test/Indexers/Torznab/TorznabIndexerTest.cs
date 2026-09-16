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
            <torznab:attr name=""peers"" value=""17"" />
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

        [Test]
        public void ParseResponse_should_parse_response_offset_and_total()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <torznab:response offset=""20"" total=""150"" />
        <item>
            <title>Paginated.Release</title>
            <link>http://indexer.local/torrent/1</link>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].ResponseOffset, Is.EqualTo(20));
            Assert.That(results[0].ResponseTotal, Is.EqualTo(150));
        }

        [Test]
        public void ParseResponse_should_parse_freeleech_and_volume_factors()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Freeleech.Release</title>
            <link>http://indexer.local/torrent/1</link>
            <torznab:attr name=""downloadvolumefactor"" value=""0"" />
            <torznab:attr name=""uploadvolumefactor"" value=""2.0"" />
        </item>
        <item>
            <title>Half.Leech.Release</title>
            <link>http://indexer.local/torrent/2</link>
            <torznab:attr name=""downloadvolumefactor"" value=""0.5"" />
            <torznab:attr name=""uploadvolumefactor"" value=""1.0"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].DownloadVolumeFactor, Is.EqualTo(0.0));
            Assert.That(results[0].UploadVolumeFactor, Is.EqualTo(2.0));
            Assert.That(results[0].IsFreeleech, Is.True);

            Assert.That(results[1].DownloadVolumeFactor, Is.EqualTo(0.5));
            Assert.That(results[1].UploadVolumeFactor, Is.EqualTo(1.0));
            Assert.That(results[1].IsFreeleech, Is.False);
        }

        [Test]
        public void ParseResponse_should_calculate_leechers_as_peers_minus_seeders()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Release.Swarm</title>
            <torznab:attr name=""seeders"" value=""30"" />
            <torznab:attr name=""peers"" value=""45"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Seeders, Is.EqualTo(30));
            Assert.That(results[0].Leechers, Is.EqualTo(15));
        }

        [Test]
        public void ParseResponse_should_clamp_leechers_to_zero_when_peers_less_than_seeders()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Release.Underflow</title>
            <torznab:attr name=""seeders"" value=""50"" />
            <torznab:attr name=""peers"" value=""20"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Seeders, Is.EqualTo(50));
            Assert.That(results[0].Leechers, Is.EqualTo(0));
        }

        [Test]
        public void ParseResponse_should_clamp_negative_seeders_and_leechers_to_zero()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Release.Negative</title>
            <torznab:attr name=""seeders"" value=""-5"" />
            <torznab:attr name=""leechers"" value=""-2"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Seeders, Is.EqualTo(0));
            Assert.That(results[0].Leechers, Is.EqualTo(0));
        }

        [Test]
        public void ParseResponse_should_reject_nan_infinity_and_negative_volume_factors()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Release.InvalidFactors</title>
            <torznab:attr name=""downloadvolumefactor"" value=""NaN"" />
            <torznab:attr name=""uploadvolumefactor"" value=""-1.0"" />
        </item>
        <item>
            <title>Test.Release.InfinityFactors</title>
            <torznab:attr name=""downloadvolumefactor"" value=""Infinity"" />
            <torznab:attr name=""uploadvolumefactor"" value=""-Infinity"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].DownloadVolumeFactor, Is.Null);
            Assert.That(results[0].UploadVolumeFactor, Is.Null);
            Assert.That(results[1].DownloadVolumeFactor, Is.Null);
            Assert.That(results[1].UploadVolumeFactor, Is.Null);
        }

        [Test]
        public void ParseResponse_should_clamp_excessive_volume_factors_to_ten()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Release.HighFactors</title>
            <torznab:attr name=""downloadvolumefactor"" value=""15.5"" />
            <torznab:attr name=""uploadvolumefactor"" value=""100.0"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].DownloadVolumeFactor, Is.EqualTo(10.0));
            Assert.That(results[0].UploadVolumeFactor, Is.EqualTo(10.0));
        }

        [TestCase("movies", "2000,2010,2020,2030,2040,2045,2050,2060,2070,2080,2090")]
        [TestCase("tv", "5000,5010,5020,5030,5040,5045,5050,5060,5070,5080")]
        [TestCase("music", "3000,3010,3020,3030,3040,3050,3060")]
        [TestCase("2000", "2000,2010,2020,2030,2040,2045,2050,2060,2070,2080,2090")]
        [TestCase("2040", "2040")]
        [TestCase("anime", "5070")]
        public void MapFriendlyCategory_should_map_and_expand_categories(string input, string expected)
        {
            var result = TorznabIndexer.MapFriendlyCategory(input);
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
