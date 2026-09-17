using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class PeerListeningPortHealthCheckTest
{
    private IPeerServer _peerServer;
    private IConfigService _configService;
    private PeerListeningPortHealthCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _peerServer = Substitute.For<IPeerServer>();
        _configService = Substitute.For<IConfigService>();
        _configService.ListeningPort.Returns(6881);

        _subject = new PeerListeningPortHealthCheck(_peerServer, _configService);
    }

    [Test]
    public void Check_should_return_ok_when_peer_server_is_listening()
    {
        _peerServer.IsListening.Returns(true);
        _peerServer.BindFailed.Returns(false);
        _peerServer.ListeningPort.Returns(6881);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_error_when_peer_server_bind_failed()
    {
        _peerServer.IsListening.Returns(false);
        _peerServer.BindFailed.Returns(true);
        _peerServer.ListeningPort.Returns(0);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("Peer server failed to listen on port 6881"));
        Assert.That(result.Message, Does.Contain("Port is already in use by another process or address could not be bound."));
    }

    [Test]
    public void Check_should_return_error_when_peer_server_is_not_listening()
    {
        _peerServer.IsListening.Returns(false);
        _peerServer.BindFailed.Returns(false);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("Peer server failed to listen on port 6881"));
    }
}
