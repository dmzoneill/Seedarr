using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Prowlarr;

namespace NzbDrone.Core.Test.Indexers.Prowlarr
{
    [TestFixture]
    public class ProwlarrIndexerTest
    {
        private ProwlarrIndexer _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new ProwlarrIndexer();
        }

        [Test]
        public void Name_should_return_prowlarr()
        {
            Assert.That(_subject.Name, Is.EqualTo("Prowlarr"));
        }

        [Test]
        public void IndexerType_should_return_prowlarr()
        {
            Assert.That(_subject.IndexerType, Is.EqualTo("Prowlarr"));
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
