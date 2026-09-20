using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class PeerPortBindingCheckTest
{
    private IPeerServer _peerServer;
    private PeerPortBindingCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _peerServer = Substitute.For<IPeerServer>();
        _subject = new PeerPortBindingCheck(_peerServer);
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
    public void Check_should_return_error_when_bind_failed()
    {
        _peerServer.IsListening.Returns(false);
        _peerServer.BindFailed.Returns(true);
        _peerServer.ListeningPort.Returns(6881);
        _peerServer.BindErrorMessage.Returns("Address already in use");

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("Port 6881 failed to bind or is already in use by another application"));
        Assert.That(result.Message, Does.Contain("Address already in use"));
        Assert.That(result.Message, Does.Contain("Please choose another port in Network Settings"));
    }

    [Test]
    public void Check_should_return_error_when_is_listening_is_false_and_peer_server_initialized()
    {
        _peerServer.IsListening.Returns(false);
        _peerServer.BindFailed.Returns(false);
        _peerServer.ListeningPort.Returns(6881);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("Port 6881 failed to bind or is already in use by another application"));
    }

    [Test]
    public void Check_should_return_ok_when_peer_server_is_null()
    {
        var subject = new PeerPortBindingCheck(null);
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }
}
