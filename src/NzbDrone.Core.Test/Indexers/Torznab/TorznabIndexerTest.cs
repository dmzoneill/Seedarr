using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

        [Test]
        public void ParseResponse_should_prohibit_dtd_entity_expansion()
        {
            var xmlWithDtd = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE foo [ <!ENTITY xxe SYSTEM ""http://169.254.169.254/latest/meta-data""> ]>
<rss version=""2.0"">
    <channel>
        <item>
            <title>&xxe;</title>
            <link>http://indexer.local/torrent/1</link>
        </item>
    </channel>
</rss>";

            Assert.Throws<System.Xml.XmlException>(() => _subject.ParseResponse(xmlWithDtd));
        }

        [Test]
        public void FetchTorrentByHash_should_reject_unsafe_enclosure_download_url()
        {
            var handler = new TorznabTestHttpMessageHandler();
            var indexer = new TorznabIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Torznab Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "secret-key"
            };

            var searchXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <item>
            <title>Unsafe Enclosure Torrent</title>
            <enclosure url=""http://169.254.169.254/latest/meta-data"" length=""1024"" />
        </item>
    </channel>
</rss>";

            handler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(searchXml)
            };

            var result = indexer.FetchTorrentByHash(definition, "0123456789abcdef0123456789abcdef01234567");

            Assert.That(result, Is.Null);
            Assert.That(handler.SentRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void FetchTorrentByHash_should_not_send_api_key_to_cross_origin_download_host()
        {
            var handler = new TorznabTestHttpMessageHandler();
            var indexer = new TorznabIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Torznab Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "secret-key"
            };

            var searchXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <item>
            <title>Cross Origin Enclosure Torrent</title>
            <enclosure url=""http://8.8.4.4:8080/download/1.torrent"" length=""1024"" />
        </item>
    </channel>
</rss>";

            var torrentBytes = new byte[] { 1, 2, 3, 4 };

            handler.Handler = req =>
            {
                if (req.RequestUri.ToString().Contains("infohash"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(searchXml)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(torrentBytes)
                };
            };

            var result = indexer.FetchTorrentByHash(definition, "0123456789abcdef0123456789abcdef01234567");

            Assert.That(result, Is.EqualTo(torrentBytes));
            Assert.That(handler.SentRequests.Count, Is.EqualTo(2));
            var dlRequest = handler.SentRequests[1];
            Assert.That(dlRequest.Headers.Contains("X-Api-Key"), Is.False);
        }

        [Test]
        public void FetchTorrentByHash_should_send_api_key_to_same_origin_download_host()
        {
            var handler = new TorznabTestHttpMessageHandler();
            var indexer = new TorznabIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Torznab Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "secret-key"
            };

            var searchXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <item>
            <title>Same Origin Enclosure Torrent</title>
            <enclosure url=""http://8.8.8.8:9696/download/1.torrent"" length=""1024"" />
        </item>
    </channel>
</rss>";

            var torrentBytes = new byte[] { 5, 6, 7, 8 };

            handler.Handler = req =>
            {
                if (req.RequestUri.ToString().Contains("infohash"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(searchXml)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(torrentBytes)
                };
            };

            var result = indexer.FetchTorrentByHash(definition, "0123456789abcdef0123456789abcdef01234567");

            Assert.That(result, Is.EqualTo(torrentBytes));
            Assert.That(handler.SentRequests.Count, Is.EqualTo(2));
            var dlRequest = handler.SentRequests[1];
            Assert.That(dlRequest.Headers.Contains("X-Api-Key"), Is.True);
            Assert.That(dlRequest.Headers.GetValues("X-Api-Key").First(), Is.EqualTo("secret-key"));
        }

        [Test]
        public void ParseResponse_should_detect_freeleech_with_various_attribute_values()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Yes.Release</title>
            <torznab:attr name=""freeleech"" value=""yes"" />
        </item>
        <item>
            <title>Test.Free.Release</title>
            <torznab:attr name=""freeleech"" value=""free"" />
        </item>
        <item>
            <title>Test.One.Release</title>
            <torznab:attr name=""freeleech"" value=""1"" />
        </item>
        <item>
            <title>Test.True.Release</title>
            <torznab:attr name=""freeleech"" value=""true"" />
        </item>
        <item>
            <title>Test.Node.Yes.Release</title>
            <torznab:freeleech>yes</torznab:freeleech>
        </item>
        <item>
            <title>Test.Node.Free.Release</title>
            <torznab:freeleech>free</torznab:freeleech>
        </item>
        <item>
            <title>Test.Node.Attr.Release</title>
            <torznab:freeleech value=""yes"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(7));
            foreach (var release in results)
            {
                Assert.That(release.DownloadVolumeFactor, Is.EqualTo(0.0));
                Assert.That(release.IsFreeleech, Is.True);
            }
        }

        [Test]
        public void ParseResponse_should_detect_freeleech_from_title_tags()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Test.Movie.2026.1080p [Freeleech]</title>
        </item>
        <item>
            <title>Test.Movie.2026.1080p [FL]</title>
        </item>
        <item>
            <title>Test.Movie.2026.1080p (Freeleech)</title>
        </item>
        <item>
            <title>Test.Movie.2026.1080p (FL)</title>
        </item>
        <item>
            <title>Test.Movie.2026.1080p Regular</title>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(5));
            Assert.That(results[0].IsFreeleech, Is.True);
            Assert.That(results[0].DownloadVolumeFactor, Is.EqualTo(0.0));

            Assert.That(results[1].IsFreeleech, Is.True);
            Assert.That(results[1].DownloadVolumeFactor, Is.EqualTo(0.0));

            Assert.That(results[2].IsFreeleech, Is.True);
            Assert.That(results[2].DownloadVolumeFactor, Is.EqualTo(0.0));

            Assert.That(results[3].IsFreeleech, Is.True);
            Assert.That(results[3].DownloadVolumeFactor, Is.EqualTo(0.0));

            Assert.That(results[4].IsFreeleech, Is.False);
            Assert.That(results[4].DownloadVolumeFactor, Is.EqualTo(1.0));
        }

        [TestCase(0.0, true)]
        [TestCase(0.0005, true)]
        [TestCase(0.001, true)]
        [TestCase(0.0011, false)]
        [TestCase(0.5, false)]
        [TestCase(1.0, false)]
        public void IsFreeleech_should_evaluate_with_floating_point_tolerance(double factor, bool expected)
        {
            var release = new ReleaseInfo { DownloadVolumeFactor = factor };
            Assert.That(release.IsFreeleech, Is.EqualTo(expected));
        }

        [Test]
        public void IsFreeleech_should_return_false_when_download_volume_factor_is_null()
        {
            var release = new ReleaseInfo { DownloadVolumeFactor = null };
            Assert.That(release.IsFreeleech, Is.False);
        }

        [Test]
        public void ParseResponse_should_parse_pubDate_with_timezone_offsets_to_utc()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Positive.Offset.Release</title>
            <pubDate>Mon, 15 Sep 2026 22:00:00 +0200</pubDate>
        </item>
        <item>
            <title>Negative.Offset.Release</title>
            <pubDate>Mon, 15 Sep 2026 12:00:00 -0500</pubDate>
        </item>
        <item>
            <title>Utc.Offset.Release</title>
            <pubDate>Mon, 15 Sep 2026 22:00:00 GMT</pubDate>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(3));

            Assert.That(results[0].PublishDate.HasValue, Is.True);
            Assert.That(results[0].PublishDate.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(results[0].PublishDate.Value, Is.EqualTo(new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc)));

            Assert.That(results[1].PublishDate.HasValue, Is.True);
            Assert.That(results[1].PublishDate.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(results[1].PublishDate.Value, Is.EqualTo(new DateTime(2026, 9, 15, 17, 0, 0, DateTimeKind.Utc)));

            Assert.That(results[2].PublishDate.HasValue, Is.True);
            Assert.That(results[2].PublishDate.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(results[2].PublishDate.Value, Is.EqualTo(new DateTime(2026, 9, 15, 22, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void ParseResponse_should_handle_omitted_seeders_as_null_and_rule_matches()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>No.Seeders.Release</title>
            <link>http://indexer.local/torrent/1</link>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Seeders, Is.Null);

            var rule = new RssRule
            {
                MinSeeders = 1,
                AllowUnknownSeeders = true
            };

            Assert.That(rule.Matches(results[0]), Is.True);

            rule.AllowUnknownSeeders = false;
            Assert.That(rule.Matches(results[0]), Is.False);
        }

        private class TorznabTestHttpMessageHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> SentRequests { get; } = new();
            public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; }

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SentRequests.Add(request);
                return Handler?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SentRequests.Add(request);
                return Task.FromResult(Handler?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
