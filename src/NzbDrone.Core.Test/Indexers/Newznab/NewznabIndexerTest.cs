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
    }
}
