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
        public void TestConnection_should_return_false_when_url_is_null()
        {
            var definition = new IndexerDefinition
            {
                Url = null,
                ApiKey = "test-key",
                ApiPath = "/api"
            };

            var result = _subject.TestConnection(definition);

            Assert.That(result, Is.False);
        }
    }
}
