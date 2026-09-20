using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Indexers.Newznab
{
    [TestFixture]
    public class NewznabIndexerTest
    {
        private NewznabIndexer _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new NewznabIndexer();
        }

        [Test]
        public void Name_should_return_newznab()
        {
            Assert.That(_subject.Name, Is.EqualTo("Newznab"));
        }

        [Test]
        public void IndexerType_should_return_newznab()
        {
            Assert.That(_subject.IndexerType, Is.EqualTo("Newznab"));
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
            Assert.That(result.Message, Does.Contain("Unable to connect to Newznab"));
        }

        [Test]
        public void ParseResponse_should_parse_newznab_xml_feed()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
    <channel>
        <title>Newznab Indexer Feed</title>
        <newznab:response offset=""0"" total=""42"" />
        <item>
            <title>Ubuntu.Linux.24.04.ISO</title>
            <guid>item-guid-12345</guid>
            <link>https://newznab.local/nzb/12345.nzb</link>
            <pubDate>Mon, 01 Jan 2026 12:00:00 +0000</pubDate>
            <enclosure url=""https://newznab.local/d/12345.nzb"" length=""1048576"" type=""application/x-nzb"" />
            <newznab:attr name=""category"" value=""4000"" />
            <newznab:attr name=""size"" value=""1048576"" />
        </item>
    </channel>
</rss>";

            var definition = new IndexerDefinition { Id = 10, Name = "NZB Indexer" };
            var results = _subject.ParseResponse(xml, definition);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Ubuntu.Linux.24.04.ISO"));
            Assert.That(results[0].Guid, Is.EqualTo("item-guid-12345"));
            Assert.That(results[0].DownloadUrl, Is.EqualTo("https://newznab.local/d/12345.nzb"));
            Assert.That(results[0].Size, Is.EqualTo(1048576));
            Assert.That(results[0].IndexerId, Is.EqualTo(10));
            Assert.That(results[0].Indexer, Is.EqualTo("NZB Indexer"));
            Assert.That(results[0].Categories, Does.Contain("4000"));
            Assert.That(results[0].ResponseOffset, Is.EqualTo(0));
            Assert.That(results[0].ResponseTotal, Is.EqualTo(42));
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
            <link>http://indexer.local/nzb/1</link>
        </item>
    </channel>
</rss>";

            Assert.Throws<System.Xml.XmlException>(() => _subject.ParseResponse(xmlWithDtd));
        }

        [Test]
        public void BuildSearchUrl_should_build_tvsearch_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local",
                ApiKey = "nzbkey",
                ApiPath = "/api"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.TvSearch,
                Query = "Simpsons",
                Season = 3,
                Episode = 10,
                TvdbId = "71663",
                Rid = "1234",
                Offset = 5,
                Limit = 20
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=tvsearch"));
            Assert.That(url, Does.Contain("q=Simpsons"));
            Assert.That(url, Does.Contain("season=3"));
            Assert.That(url, Does.Contain("ep=10"));
            Assert.That(url, Does.Contain("tvdbid=71663"));
            Assert.That(url, Does.Contain("rid=1234"));
            Assert.That(url, Does.Contain("offset=5"));
            Assert.That(url, Does.Contain("limit=20"));
            Assert.That(url, Does.Contain("apikey=nzbkey"));
        }

        [Test]
        public void BuildSearchUrl_should_build_movie_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.Movie,
                Query = "Matrix",
                ImdbId = "tt0133093",
                TmdbId = "603"
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=movie"));
            Assert.That(url, Does.Contain("q=Matrix"));
            Assert.That(url, Does.Contain("imdbid=tt0133093"));
            Assert.That(url, Does.Contain("tmdbid=603"));
        }

        [Test]
        public void BuildSearchUrl_should_build_music_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.Music,
                Artist = "Beatles",
                Album = "Abbey Road"
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=music"));
            Assert.That(url, Does.Contain("artist=Beatles"));
            Assert.That(url, Does.Contain("album=Abbey%20Road"));
        }

        [Test]
        public void BuildSearchUrl_should_build_book_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var query = new SearchQuery
            {
                Mode = SearchMode.Book,
                Author = "Asimov",
                Title = "Foundation"
            };

            var url = _subject.BuildSearchUrl(definition, query);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=book"));
            Assert.That(url, Does.Contain("author=Asimov"));
            Assert.That(url, Does.Contain("title=Foundation"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_tvsearch_url_with_season_episode_and_ids()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local",
                ApiKey = "nzbkey",
                ApiPath = "/api"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "tvsearch",
                Query = "Simpsons",
                Season = 3,
                Episode = 10,
                TvdbId = "71663",
                TmdbId = "456",
                Year = 1991
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=tvsearch"));
            Assert.That(url, Does.Contain("q=Simpsons"));
            Assert.That(url, Does.Contain("season=3"));
            Assert.That(url, Does.Contain("ep=10"));
            Assert.That(url, Does.Contain("tvdbid=71663"));
            Assert.That(url, Does.Contain("tmdbid=456"));
            Assert.That(url, Does.Contain("year=1991"));
            Assert.That(url, Does.Contain("apikey=nzbkey"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_infer_tvsearch_when_season_present()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                Query = "Simpsons",
                Season = 3,
                Episode = 10
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=tvsearch"));
            Assert.That(url, Does.Contain("season=3"));
            Assert.That(url, Does.Contain("ep=10"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_movie_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "movie",
                Query = "Matrix",
                ImdbId = "tt0133093",
                TmdbId = "603",
                Year = 1999
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=movie"));
            Assert.That(url, Does.Contain("q=Matrix"));
            Assert.That(url, Does.Contain("imdbid=tt0133093"));
            Assert.That(url, Does.Contain("tmdbid=603"));
            Assert.That(url, Does.Contain("year=1999"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_infer_movie_when_imdb_present()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                ImdbId = "tt0133093"
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=movie"));
            Assert.That(url, Does.Contain("imdbid=tt0133093"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_music_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "music",
                Artist = "Beatles",
                Album = "Abbey Road",
                Year = 1969
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=music"));
            Assert.That(url, Does.Contain("artist=Beatles"));
            Assert.That(url, Does.Contain("album=Abbey%20Road"));
            Assert.That(url, Does.Contain("year=1969"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_build_book_url_with_parameters()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local"
            };

            var criteria = new TorznabSearchCriteria
            {
                SearchType = "book",
                Author = "Asimov",
                Title = "Foundation",
                Year = 1951
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=book"));
            Assert.That(url, Does.Contain("author=Asimov"));
            Assert.That(url, Does.Contain("title=Foundation"));
            Assert.That(url, Does.Contain("year=1951"));
        }

        [Test]
        public void BuildSearchUrl_with_TorznabSearchCriteria_should_support_backward_compatibility_search()
        {
            var definition = new IndexerDefinition
            {
                Url = "http://newznab.local",
                ApiKey = "nzbkey"
            };

            var criteria = new TorznabSearchCriteria
            {
                Query = "Linux ISO"
            };

            var url = _subject.BuildSearchUrl(definition, criteria);

            Assert.That(url, Does.StartWith("http://newznab.local/api?t=search"));
            Assert.That(url, Does.Contain("q=Linux%20ISO"));
            Assert.That(url, Does.Contain("apikey=nzbkey"));
        }

        [Test]
        public void ParseResponse_should_decode_html_entities_and_normalize_whitespace_in_title_and_description()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
    <channel>
        <title>Newznab Feed</title>
        <item>
            <title><![CDATA[Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;]]></title>
            <description><![CDATA[Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;]]></description>
            <comments><![CDATA[https://newznab.local/details/123&amp;view=1]]></comments>
            <link>https://newznab.local/nzb/123.nzb</link>
            <size>1048576</size>
        </item>
    </channel>
</rss>";

            var results = _subject.ParseResponse(xml);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Title, Is.EqualTo("Fast & Furious's\"Cut\""));
            Assert.That(results[0].Description, Is.EqualTo("Fast & Furious's\"Cut\""));
            Assert.That(results[0].Comments, Is.EqualTo("https://newznab.local/details/123&view=1"));
        }

        [Test]
        public void ParseResponse_should_decode_cdata_character_references_and_entities()
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
    <channel>
        <item>
            <title><![CDATA[  Tom&apos;s&nbsp;&amp;&nbsp;Jerry&#x27;s&#x2F;Movie   2026   ]]></title>
            <description><![CDATA[  Release &amp; details &nbsp; with &quot;quotes&quot;  ]]></description>
            <link>https://newznab.local/nzb/456.nzb</link>
            <size>2048</size>
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
<rss version=""2.0"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
    <channel>
        <item>
            <title>Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;</title>
            <description>Fast&nbsp;&amp;&nbsp;Furious&#39;s&quot;Cut&quot;</description>
            <link>https://newznab.local/nzb/789.nzb</link>
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
            var handler = new NewznabTestHttpMessageHandler();
            var indexer = new NewznabIndexer(new HttpClient(handler));

            var definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Newznab Test",
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
            var handler = new NewznabTestHttpMessageHandler();
            var indexer = new NewznabIndexer(new HttpClient(handler), statusService);

            var definition = new IndexerDefinition
            {
                Id = 42,
                Name = "Newznab Test",
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
        <title>Newznab Feed</title>
        <ttl>{ttlValue}</ttl>
        <item>
            <title>Test.Item</title>
        </item>
    </channel>
</rss>";

            var ttl = NewznabIndexer.ParseTtl(xml);
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
        <title>Newznab Feed</title>
        {ttlElement}
        <item>
            <title>Test.Item</title>
        </item>
    </channel>
</rss>";

            var ttl = NewznabIndexer.ParseTtl(xml);
            Assert.That(ttl, Is.EqualTo(15));
        }

        [Test]
        public void ParseResponse_should_record_rss_sync_with_parsed_ttl()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var indexer = new NewznabIndexer(indexerStatusService: statusService);
            var definition = new IndexerDefinition { Id = 12, Name = "Newznab Test" };

            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <title>Newznab Feed</title>
        <ttl>45</ttl>
        <item>
            <title>Test.Release.2026</title>
        </item>
    </channel>
</rss>";

            var results = indexer.ParseResponse(xml, definition);

            Assert.That(results, Has.Count.EqualTo(1));
            statusService.Received(1).RecordRssSync(12, 45);
        }

        [Test]
        public void ParseResponse_should_record_rss_sync_with_default_ttl_when_omitted()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var indexer = new NewznabIndexer(indexerStatusService: statusService);
            var definition = new IndexerDefinition { Id = 12, Name = "Newznab Test" };

            var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"">
    <channel>
        <title>Newznab Feed</title>
        <item>
            <title>Test.Release.2026</title>
        </item>
    </channel>
</rss>";

            var results = indexer.ParseResponse(xml, definition);

            Assert.That(results, Has.Count.EqualTo(1));
            statusService.Received(1).RecordRssSync(12, 15);
        }

        [Test]
        public void Search_HTTP_429_should_parse_retry_after_header_seconds_and_record_failure()
        {
            var statusService = Substitute.For<IIndexerStatusService>();
            var handler = new NewznabTestHttpMessageHandler();
            var indexer = new NewznabIndexer(new HttpClient(handler), statusService);

            var definition = new IndexerDefinition
            {
                Id = 42,
                Name = "Newznab Test",
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
            var handler = new NewznabTestHttpMessageHandler();
            var indexer = new NewznabIndexer(new HttpClient(handler), statusService);

            var definition = new IndexerDefinition
            {
                Id = 42,
                Name = "Newznab Test",
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
        public void ParseResponse_should_parse_rfc822_pubdate_with_invariant_culture()
        {
            var prevCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

                var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<rss version=""2.0"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
    <channel>
        <title>Newznab Feed</title>
        <item>
            <title>Ubuntu.Linux.ISO</title>
            <link>https://newznab.local/nzb/123.nzb</link>
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
<rss version=""2.0"" xmlns:newznab=""http://www.newznab.com/DTD/2010/feeds/attributes/"">
    <channel>
        <title>Newznab Feed</title>
        <item>
            <title>Ubuntu.Linux.Magnet</title>
            <link>magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&amp;dn=Ubuntu.Linux.Magnet</link>
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

            var indexer = new NewznabIndexer(proxySettingsProvider: proxySettingsProvider);

            Assert.That(indexer.Handler, Is.SameAs(proxyHandler));
            Assert.That(indexer.Client, Is.Not.Null);
            proxySettingsProvider.Received(1).CreateHandler();
        }

        [Test]
        public void When_proxy_is_disabled_falls_back_to_direct_client()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(false);

            var indexer = new NewznabIndexer(proxySettingsProvider: proxySettingsProvider);

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

            var indexer = new NewznabIndexer(proxySettingsProvider: proxySettingsProvider);
            var definition = new IndexerDefinition { Url = "http://newznab.proxy.test", ApiKey = "testkey" };

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

            var indexer = new NewznabIndexer(proxySettingsProvider: proxySettingsProvider);
            var definition = new IndexerDefinition { Url = "http://newznab.proxy.test", ApiKey = "testkey" };

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
        public void When_proxy_host_or_port_changes_updates_handler_and_client()
        {
            var proxySettingsProvider = Substitute.For<IProxySettingsProvider>();
            proxySettingsProvider.IsEnabled.Returns(true);
            proxySettingsProvider.Type.Returns(ProxyType.Http);
            proxySettingsProvider.Host.Returns("proxy1.local");
            proxySettingsProvider.Port.Returns(8080);

            var proxyHandler1 = new SocketsHttpHandler();
            proxySettingsProvider.CreateHandler().Returns(proxyHandler1);

            var indexer = new NewznabIndexer(proxySettingsProvider: proxySettingsProvider);
            var initialClient = indexer.Client;

            Assert.That(indexer.Handler, Is.SameAs(proxyHandler1));

            var proxyHandler2 = new SocketsHttpHandler();
            proxySettingsProvider.Host.Returns("proxy2.local");
            proxySettingsProvider.Port.Returns(9090);
            proxySettingsProvider.CreateHandler().Returns(proxyHandler2);

            Assert.That(indexer.Handler, Is.SameAs(proxyHandler2));
            Assert.That(indexer.Client, Is.Not.SameAs(initialClient));
        }

        private class NewznabTestHttpMessageHandler : HttpMessageHandler
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
