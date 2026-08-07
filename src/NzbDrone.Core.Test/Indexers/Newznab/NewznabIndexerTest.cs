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
    }
}
