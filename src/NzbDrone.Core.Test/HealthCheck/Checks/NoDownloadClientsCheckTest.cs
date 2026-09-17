using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class NoDownloadClientsCheckTest
{
    private IDownloadClientFactory _downloadClientFactory;
    private NoDownloadClientsCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _downloadClientFactory = Substitute.For<IDownloadClientFactory>();
        _subject = new NoDownloadClientsCheck(_downloadClientFactory);
    }

    [Test]
    public void Check_should_return_notice_when_no_clients()
    {
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>());

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoDownloadClients"));
    }

    [Test]
    public void Check_should_return_notice_when_clients_exist_but_none_enabled()
    {
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new DownloadClientDefinition { Enable = false }
        });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoDownloadClients"));
    }

    [Test]
    public void Check_should_return_ok_when_enabled_client_exists()
    {
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition>
        {
            new DownloadClientDefinition { Enable = true }
        });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(result.Source, Is.EqualTo("NoDownloadClients"));
    }
}
