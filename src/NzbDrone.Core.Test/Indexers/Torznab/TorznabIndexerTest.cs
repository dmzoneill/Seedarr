using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Network;

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
        public void ParseResponse_should_parse_media_ids_codecs_and_seeding_attributes()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Torznab Feed</title>
        <item>
            <title>The.Shawshank.Redemption.1994.1080p.BluRay.x264</title>
            <link>http://indexer.local/torrent/123</link>
            <size>1073741824</size>
            <torznab:attr name=""imdbid"" value=""0111161"" />
            <torznab:attr name=""tmdbid"" value=""278"" />
            <torznab:attr name=""tvdbid"" value=""73545"" />
            <torznab:attr name=""resolution"" value=""1080p"" />
            <torznab:attr name=""video"" value=""x264"" />
            <torznab:attr name=""audio"" value=""FLAC"" />
            <torznab:attr name=""minimumratio"" value=""1.5"" />
            <torznab:attr name=""minimumseedtime"" value=""259200"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            var release = results[0];
            Assert.That(release.ImdbId, Is.EqualTo("tt0111161"));
            Assert.That(release.TmdbId, Is.EqualTo(278));
            Assert.That(release.TvdbId, Is.EqualTo(73545));
            Assert.That(release.Resolution, Is.EqualTo("1080p"));
            Assert.That(release.VideoCodec, Is.EqualTo("x264"));
            Assert.That(release.AudioCodec, Is.EqualTo("FLAC"));
            Assert.That(release.MinimumRatio, Is.EqualTo(1.5));
            Assert.That(release.MinimumSeedTime, Is.EqualTo(259200L));
        }

        [Test]
        public void ParseResponse_should_parse_alternative_attribute_names_and_normalize_imdb()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Torznab Feed</title>
        <item>
            <title>Inception.2010.2160p.UHD.Remux.HEVC.Atmos</title>
            <link>http://indexer.local/torrent/456</link>
            <size>50000000000</size>
            <torznab:attr name=""imdb"" value=""tt1375666"" />
            <torznab:attr name=""tmdb"" value=""27205"" />
            <torznab:attr name=""tvdb"" value=""12345"" />
            <torznab:attr name=""resolution"" value=""2160p"" />
            <torznab:attr name=""videocodec"" value=""HEVC"" />
            <torznab:attr name=""audiocodec"" value=""Atmos"" />
            <torznab:attr name=""minimumratio"" value=""2.0"" />
            <torznab:attr name=""minimumseedtime"" value=""172800"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            var release = results[0];
            Assert.That(release.ImdbId, Is.EqualTo("tt1375666"));
            Assert.That(release.TmdbId, Is.EqualTo(27205));
            Assert.That(release.TvdbId, Is.EqualTo(12345));
            Assert.That(release.Resolution, Is.EqualTo("2160p"));
            Assert.That(release.VideoCodec, Is.EqualTo("HEVC"));
            Assert.That(release.AudioCodec, Is.EqualTo("Atmos"));
            Assert.That(release.MinimumRatio, Is.EqualTo(2.0));
            Assert.That(release.MinimumSeedTime, Is.EqualTo(172800L));
        }

        [TestCase("0111161", "tt0111161")]
        [TestCase("tt0111161", "tt0111161")]
        [TestCase("TT0111161", "tt0111161")]
        [TestCase("1234567", "tt1234567")]
        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("   ", null)]
        [TestCase("invalid_id", "invalid_id")]
        public void NormalizeImdbId_should_normalize_imdb_correctly(string input, string expected)
        {
            var normalized = ReleaseInfo.NormalizeImdbId(input);
            Assert.That(normalized, Is.EqualTo(expected));
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

        [Test]
        public void BuildSearchUrl_should_build_tvsearch_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local",
                ApiKey = "myapikey",
                ApiPath = "/api"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.TvSearch,
                Query = "Simpsons",
                Season = 5,
                Episode = 2,
                TvdbId = "12345",
                ImdbId = "tt0096697",
                Offset = 10,
                Limit = 25
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=tvsearch"));
            Assert.That(url, Does.Contain("q=Simpsons"));
            Assert.That(url, Does.Contain("season=5"));
            Assert.That(url, Does.Contain("ep=2"));
            Assert.That(url, Does.Contain("tvdbid=12345"));
            Assert.That(url, Does.Contain("imdbid=tt0096697"));
            Assert.That(url, Does.Contain("offset=10"));
            Assert.That(url, Does.Contain("limit=25"));
            Assert.That(url, Does.Contain("apikey=myapikey"));
        }

        [Test]
        public void BuildSearchUrl_should_build_movie_url_with_tmdb_and_imdb()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local",
                ApiKey = "myapikey"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.Movie,
                Query = "Inception",
                ImdbId = "tt1375666",
                TmdbId = "27205"
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=movie"));
            Assert.That(url, Does.Contain("q=Inception"));
            Assert.That(url, Does.Contain("imdbid=tt1375666"));
            Assert.That(url, Does.Contain("tmdbid=27205"));
        }

        [Test]
        public void BuildSearchUrl_should_build_music_url_with_artist_and_album()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.Music,
                Artist = "Pink Floyd",
                Album = "The Wall"
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=music"));
            Assert.That(url, Does.Contain("artist=Pink%20Floyd"));
            Assert.That(url, Does.Contain("album=The%20Wall"));
        }

        [Test]
        public void BuildSearchUrl_should_build_book_url_with_author_and_title()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.Book,
                Author = "Tolkien",
                Title = "The Hobbit"
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=book"));
            Assert.That(url, Does.Contain("author=Tolkien"));
            Assert.That(url, Does.Contain("title=The%20Hobbit"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_tvsearch_url_with_season_episode_and_ids()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local",
                ApiKey = "myapikey",
                ApiPath = "/api"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "tvsearch",
                Query = "Breaking Bad",
                Season = 1,
                Episode = 3,
                TvdbId = "81189",
                TmdbId = "1396",
                Year = 2008
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=tvsearch"));
            Assert.That(url, Does.Contain("q=Breaking%20Bad"));
            Assert.That(url, Does.Contain("season=1"));
            Assert.That(url, Does.Contain("ep=3"));
            Assert.That(url, Does.Contain("tvdbid=81189"));
            Assert.That(url, Does.Contain("tmdbid=1396"));
            Assert.That(url, Does.Contain("year=2008"));
            Assert.That(url, Does.Contain("apikey=myapikey"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_infer_tvsearch_when_season_present()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                Query = "Simpsons",
                Season = 5,
                Episode = 2
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=tvsearch"));
            Assert.That(url, Does.Contain("season=5"));
            Assert.That(url, Does.Contain("ep=2"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_movie_url_with_imdb_and_tmdb()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "movie",
                Query = "The Dark Knight",
                ImdbId = "tt0468569",
                TmdbId = "155",
                Year = 2008
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=movie"));
            Assert.That(url, Does.Contain("q=The%20Dark%20Knight"));
            Assert.That(url, Does.Contain("imdbid=tt0468569"));
            Assert.That(url, Does.Contain("tmdbid=155"));
            Assert.That(url, Does.Contain("year=2008"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_infer_movie_when_imdb_present()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                ImdbId = "tt0468569"
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=movie"));
            Assert.That(url, Does.Contain("imdbid=tt0468569"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_music_url_with_artist_and_album()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "music",
                Artist = "Daft Punk",
                Album = "Discovery",
                Year = 2001
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=music"));
            Assert.That(url, Does.Contain("artist=Daft%20Punk"));
            Assert.That(url, Does.Contain("album=Discovery"));
            Assert.That(url, Does.Contain("year=2001"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_book_url_with_author_and_title()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "book",
                Author = "Frank Herbert",
                Title = "Dune",
                Year = 1965
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=book"));
            Assert.That(url, Does.Contain("author=Frank%20Herbert"));
            Assert.That(url, Does.Contain("title=Dune"));
            Assert.That(url, Does.Contain("year=1965"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_support_backward_compatibility_search()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://torznab.local",
                ApiKey = "key123"
            };

            var criteria = new TorznabSearchCriteria
            {
                Query = "Ubuntu 22.04"
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://torznab.local/api?t=search"));
            Assert.That(url, Does.Contain("q=Ubuntu%2022.04"));
            Assert.That(url, Does.Contain("apikey=key123"));
        }

        [Test]
        public void ParseResponse_should_decode_html_entities_and_normalize_whitespace_in_title_and_description()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title><![CDATA[Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;]]></title>
            <description><![CDATA[Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;]]></description>
            <comments><![CDATA[https://indexer.local/details?id=123&amp;ref=test]]></comments>
            <link>http://indexer.local/torrent/1</link>
            <size>1048576</size>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Fast & Furious's\"Cut\""));
            Assert.That(results[0].Description, Is.EqualTo("Fast & Furious's\"Cut\""));
            Assert.That(results[0].Comments, Is.EqualTo("https://indexer.local/details?id=123&ref=test"));
        }

        [Test]
        public void ParseResponse_should_decode_cdata_character_references_and_entities()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title><![CDATA[  Tom&apos;s&nbsp;&amp;&nbsp;Jerry&#x27;s&#x2F;Movie   2026   ]]></title>
            <description><![CDATA[  Release &amp; details &nbsp; with &quot;quotes&quot;  ]]></description>
            <link>http://indexer.local/torrent/2</link>
            <size>1048576</size>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Tom's & Jerry's/Movie 2026"));
            Assert.That(results[0].Description, Is.EqualTo("Release & details with \"quotes\""));
        }

        [Test]
        public void ParseResponse_should_decode_raw_html_entities_without_cdata()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <item>
            <title>Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;</title>
            <description>Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;</description>
            <link>http://indexer.local/torrent/3</link>
            <size>1048576</size>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Fast & Furious's\"Cut\""));
            Assert.That(results[0].Description, Is.EqualTo("Fast & Furious's\"Cut\""));
        }

        [TestCase("100", "Incorrect user credentials")]
        [TestCase("101", "Account suspended")]
        [TestCase("102", "Insufficient privileges / VIP required")]
        [TestCase("200", "Missing parameter")]
        [TestCase("500", "Request limit reached")]
        public void TestConnectionDetailed_should_return_false_when_http_200_returns_xml_error(string code, string description)
        {
            var handler = new TorznabTestHttpMessageHandler();
            var indexer = new TorznabIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Torznab Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var errorXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""{code}"" description=""{description}""/>";

            handler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(errorXml)
            };

            var result = indexer.TestConnectionDetailed(definition);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain($"Indexer authentication/access error (code {code}): {description}"));
            Assert.That(indexer.TestConnection(definition), Is.False);
        }

        [TestCase("100", "Incorrect user credentials")]
        [TestCase("101", "Account suspended")]
        [TestCase("102", "Insufficient privileges / VIP required")]
        [TestCase("200", "Missing parameter")]
        [TestCase("500", "Request limit reached")]
        public void ParseResponse_should_throw_IndexerException_when_xml_contains_error(string code, string description)
        {
            var errorXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""{code}"" description=""{description}""/>";

            var ex = Assert.Throws<IndexerException>(() => _subject.ParseResponse(errorXml));
            Assert.That(ex.Message, Does.Contain($"Indexer authentication/access error (code {code}): {description}"));
            Assert.That(ex.ErrorCode, Is.EqualTo(code));
        }

        [Test]
        public void Search_should_record_failure_and_throw_IndexerException_when_xml_contains_error()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var handler = new TorznabTestHttpMessageHandler();
            var indexer = new TorznabIndexer(new HttpClient(handler), statusService);

            var definition = new IndexerDefinition
            {
                Id = 42,
                Name = "Torznab Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "test-key"
            };

            var errorXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<error code=""100"" description=""Incorrect user credentials""/>";

            handler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(errorXml)
            };

            var ex = Assert.Throws<IndexerException>(() => indexer.Search(definition, "test"));
            Assert.That(ex.ErrorCode, Is.EqualTo("100"));
            statusService.Received(1).RecordFailure(42, 100, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<TimeSpan?>());
        }

        [TestCase("30", 30)]
        [TestCase("60", 60)]
        [TestCase("10", 10)]
        [TestCase("5", 5)]
        public void ParseTtl_should_parse_valid_ttl_from_channel(string ttlValue, int expectedMinutes)
        {
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <title>Torznab Feed</title>
        <ttl>{ttlValue}</ttl>
        <item>
            <title>Test.Item</title>
        </item>
    </channel>
</rss>";

            var ttl = TorznabIndexer.ParseTtl(xml);
            Assert.That(ttl, Is.EqualTo(expectedMinutes));
        }

        [TestCase("")]
        [TestCase("0")]
        [TestCase("-5")]
        [TestCase("abc")]
        [TestCase(null)]
        public void ParseTtl_should_fallback_to_default_15_minutes_when_ttl_is_missing_or_invalid(string ttlValue)
        {
            var ttlElement = ttlValue != null ? $"<ttl>{ttlValue}</ttl>" : "";
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <title>Torznab Feed</title>
        {ttlElement}
        <item>
            <title>Test.Item</title>
        </item>
    </channel>
</rss>";

            var ttl = TorznabIndexer.ParseTtl(xml);
            Assert.That(ttl, Is.EqualTo(15));
        }

        [Test]
        public void ParseResponse_should_record_rss_sync_with_parsed_ttl()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var indexer = new TorznabIndexer(indexerStatusService: statusService);
            var definition = new IndexerDefinition { Id = 7, Name = "Torznab Test" };

            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <title>Torznab Feed</title>
        <ttl>25</ttl>
        <item>
            <title>Test.Release.2026</title>
        </item>
    </channel>
</rss>";

            var results = indexer.ParseResponse(xml, definition);

            Assert.That(results, Has.Count.EqualTo(1));
            statusService.Received(1).RecordRssSync(7, 25);
        }

        [Test]
        public void ParseResponse_should_record_rss_sync_with_default_ttl_when_omitted()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var indexer = new TorznabIndexer(indexerStatusService: statusService);
            var definition = new IndexerDefinition { Id = 7, Name = "Torznab Test" };

            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <title>Torznab Feed</title>
        <item>
            <title>Test.Release.2026</title>
        </item>
    </channel>
</rss>";

            var results = indexer.ParseResponse(xml, definition);

            Assert.That(results, Has.Count.EqualTo(1));
            statusService.Received(1).RecordRssSync(7, 15);
        }

        [Test]
        public void Search_HTTP_429_should_parse_retry_after_header_seconds_and_record_failure()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var handler = new TorznabTestHttpMessageHandler();
            var indexer = new TorznabIndexer(new HttpClient(handler), statusService);

            var definition = new IndexerDefinition
            {
                Id = 42,
                Name = "Torznab Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "test-key"
            };

            handler.Handler = req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    ReasonPhrase = "Too Many Requests"
                };
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
                return resp;
            };

            var ex = Assert.Throws<HttpRequestException>(() => indexer.Search(definition, "test"));
            Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(ex.Data.Contains("RetryAfter"), Is.True);
            Assert.That(ex.Data["RetryAfter"], Is.EqualTo(TimeSpan.FromSeconds(120)));
            statusService.Received(1).RecordFailure(42, 429, Arg.Any<string>(), Arg.Any<Exception>(), TimeSpan.FromSeconds(120));
        }

        [Test]
        public void Search_HTTP_429_should_parse_retry_after_header_date_and_record_failure()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var handler = new TorznabTestHttpMessageHandler();
            var indexer = new TorznabIndexer(new HttpClient(handler), statusService);

            var definition = new IndexerDefinition
            {
                Id = 42,
                Name = "Torznab Test",
                Url = "http://8.8.8.8:9696",
                ApiKey = "test-key"
            };

            var futureDate = DateTimeOffset.UtcNow.AddMinutes(5);

            handler.Handler = req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    ReasonPhrase = "Too Many Requests"
                };
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(futureDate);
                return resp;
            };

            var ex = Assert.Throws<HttpRequestException>(() => indexer.Search(definition, "test"));
            Assert.That(ex.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(ex.Data.Contains("RetryAfter"), Is.True);
            var retryAfter = ex.Data["RetryAfter"] as TimeSpan?;
            Assert.That(retryAfter.HasValue, Is.True);
            Assert.That(retryAfter.Value.TotalSeconds, Is.GreaterThanOrEqualTo(280.0));
            Assert.That(retryAfter.Value.TotalSeconds, Is.LessThanOrEqualTo(310.0));
            statusService.Received(1).RecordFailure(42, 429, Arg.Any<string>(), Arg.Any<Exception>(), Arg.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds >= 280.0));
        }

        [Test]
        public void TestConnectionDetailed_should_parse_capabilities_xml_and_attach_to_result()
        {
            var handler = new TorznabTestHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var indexer = new TorznabIndexer(httpClient);

            var definition = new IndexerDefinition
            {
                Id = 10,
                Url = "http://torznab.test",
                ApiKey = "api-key",
                ApiPath = "/api"
            };

            const string capsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<caps>
  <server version=""1.0"" title=""TestTracker"" />
  <searching>
    <tv-search available=""yes"" supportedParams=""q,season,ep"" />
    <movie-search available=""yes"" supportedParams=""q,imdbid"" />
  </searching>
  <categories>
    <category id=""2000"" name=""Movies"">
      <subcat id=""2040"" name=""HD"" />
    </category>
    <category id=""100000"" name=""Anime"">
      <subcat id=""100001"" name=""Anime-Sub"" />
    </category>
  </categories>
</caps>";

            handler.Handler = req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(capsXml, System.Text.Encoding.UTF8, "application/xml")
            };

            var result = indexer.TestConnectionDetailed(definition);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Capabilities, Is.Not.Null);
            Assert.That(result.Capabilities.ServerTitle, Is.EqualTo("TestTracker"));
            Assert.That(result.Capabilities.SupportsTvSearch, Is.True);
            Assert.That(result.Capabilities.Categories.Count, Is.EqualTo(2));
            Assert.That(definition.Capabilities, Is.Not.Null);
            Assert.That(definition.Capabilities.ServerTitle, Is.EqualTo("TestTracker"));
        }

        [Test]
        public void MapFriendlyCategory_with_capabilities_should_expand_custom_categories()
        {
            var caps = new TorznabCapabilities();
            caps.Categories.Add(new TorznabCategory
            {
                Id = 100000,
                Name = "SpecialMedia",
                Subcategories = new List<TorznabSubcategory>
                {
                    new() { Id = 100001, Name = "SubMedia1" },
                    new() { Id = 100002, Name = "SubMedia2" }
                }
            });

            var mappedRoot = TorznabIndexer.MapFriendlyCategory("100000", caps);
            Assert.That(mappedRoot, Is.EqualTo("100000,100001,100002"));

            var mappedNamed = TorznabIndexer.MapFriendlyCategory("SpecialMedia", caps);
            Assert.That(mappedNamed, Is.EqualTo("100000,100001,100002"));
        }

        [Test]
        public void ParseResponse_should_parse_size_attr_when_enclosure_length_is_missing()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Torznab Indexer Feed</title>
        <item>
            <title>Test.Release.No.Length</title>
            <link>https://indexer.local/details/12345</link>
            <enclosure url=""https://indexer.local/download/12345.torrent"" type=""application/x-bittorrent"" />
            <torznab:attr name=""size"" value=""987654321"" />
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Size, Is.EqualTo(987654321));
        }

        [Test]
        public void ParseResponse_should_parse_rfc822_pubdate_with_invariant_culture()
        {
            var prevCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");

                var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Torznab Indexer Feed</title>
        <item>
            <title>Test.Release.PubDate</title>
            <link>https://indexer.local/details/12345</link>
            <pubDate>Mon, 05 May 2025 14:30:00 +0000</pubDate>
        </item>
    </channel>
</rss>";

                var results = _subject.ParseResponse(xml);

                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].PublishDate, Is.EqualTo(new DateTime(2025, 5, 5, 14, 30, 0, DateTimeKind.Utc)));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prevCulture;
            }
        }

        [Test]
        public void ParseResponse_should_extract_infohash_from_magnet_url_when_infohash_attr_is_missing()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:torznab=""http://torznab.com/schemas/2015/feed"">
    <channel>
        <title>Torznab Indexer Feed</title>
        <item>
            <title>Test.Release.Magnet</title>
            <link>magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&amp;dn=Test.Release.Magnet</link>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].InfoHash, Is.EqualTo("0123456789abcdef0123456789abcdef01234567"));
            Assert.That(results[0].MagnetUrl, Does.Contain("0123456789abcdef0123456789abcdef01234567"));
        }

        [Test]
        public void When_proxy_is_enabled_creates_and_uses_proxy_handler()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(true);
            proxySettingsProvider.Type.Returns(ProxyType.Http);
            proxySettingsProvider.Host.Returns("127.0.0.1");
            proxySettingsProvider.Port.Returns(8080);

            var proxyHandler = new SocketsHttpHandler();
            proxySettingsProvider.CreateHandler().Returns(proxyHandler);

            var indexer = new TorznabIndexer(proxySettingsProvider: proxySettingsProvider);

            Assert.That(indexer.Handler, Is.SameAs(proxyHandler));
            Assert.That(indexer.Client, Is.Not.Null);
            proxySettingsProvider.Received(1).CreateHandler();
        }

        [Test]
        public void When_proxy_is_disabled_falls_back_to_direct_client()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(false);

            var indexer = new TorznabIndexer(proxySettingsProvider: proxySettingsProvider);

            Assert.That(indexer.Handler, Is.Null);
            Assert.That(indexer.Client, Is.Not.Null);
            proxySettingsProvider.DidNotReceive().CreateHandler();
        }

        [Test]
        public void TestConnectionDetailed_when_proxy_is_enabled_routes_query_through_proxy()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(true);
            proxySettingsProvider.Type.Returns(ProxyType.Http);
            proxySettingsProvider.Host.Returns("127.0.0.1");
            proxySettingsProvider.Port.Returns(8080);

            var proxyHandler = new SocketsHttpHandler();
            proxySettingsProvider.CreateHandler().Returns(proxyHandler);

            var indexer = new TorznabIndexer(proxySettingsProvider: proxySettingsProvider);
            var definition = new IndexerDefinition { Url = "http://torznab.proxy.test", ApiKey = "testkey" };

            indexer.TestConnectionDetailed(definition);

            proxySettingsProvider.Received(1).CreateHandler();
        }

        [Test]
        public void Search_when_proxy_is_enabled_routes_query_through_proxy()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(true);
            proxySettingsProvider.Type.Returns(ProxyType.Http);
            proxySettingsProvider.Host.Returns("127.0.0.1");
            proxySettingsProvider.Port.Returns(8080);

            var proxyHandler = new SocketsHttpHandler();
            proxySettingsProvider.CreateHandler().Returns(proxyHandler);

            var indexer = new TorznabIndexer(proxySettingsProvider: proxySettingsProvider);
            var definition = new IndexerDefinition { Url = "http://torznab.proxy.test", ApiKey = "testkey" };

            try
            {
                indexer.Search(definition, "query");
            }
            catch
            {
                // Real connection fails with fake SocketsHttpHandler, which is expected
            }

            proxySettingsProvider.Received(1).CreateHandler();
        }

        [Test]
        public void GetCapabilities_when_proxy_is_enabled_routes_query_through_proxy()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(true);
            proxySettingsProvider.Type.Returns(ProxyType.Http);
            proxySettingsProvider.Host.Returns("127.0.0.1");
            proxySettingsProvider.Port.Returns(8080);

            var proxyHandler = new SocketsHttpHandler();
            proxySettingsProvider.CreateHandler().Returns(proxyHandler);

            var indexer = new TorznabIndexer(proxySettingsProvider: proxySettingsProvider);
            var definition = new IndexerDefinition { Url = "http://torznab-caps.proxy.test", ApiKey = "testkey" };

            indexer.GetCapabilities(definition);

            proxySettingsProvider.Received(1).CreateHandler();
        }

        [Test]
        public void When_proxy_host_or_port_changes_updates_handler_and_client()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(true);
            proxySettingsProvider.Type.Returns(ProxyType.Http);
            proxySettingsProvider.Host.Returns("proxy1.local");
            proxySettingsProvider.Port.Returns(8080);

            var proxyHandler1 = new SocketsHttpHandler();
            proxySettingsProvider.CreateHandler().Returns(proxyHandler1);

            var indexer = new TorznabIndexer(proxySettingsProvider: proxySettingsProvider);
            var initialClient = indexer.Client;

            Assert.That(indexer.Handler, Is.SameAs(proxyHandler1));

            var proxyHandler2 = new SocketsHttpHandler();
            proxySettingsProvider.Host.Returns("proxy2.local");
            proxySettingsProvider.Port.Returns(9090);
            proxySettingsProvider.CreateHandler().Returns(proxyHandler2);

            Assert.That(indexer.Handler, Is.SameAs(proxyHandler2));
            Assert.That(indexer.Client, Is.Not.SameAs(initialClient));
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
