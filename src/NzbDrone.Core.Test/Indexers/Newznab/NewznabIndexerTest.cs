using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;

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
    }
}
