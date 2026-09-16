using System;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Dht;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;
using Seedarr.Api.V1.Network;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class NetworkControllerDiagnosticsTest
{
    private INetworkStatusService _networkStatusService;
    private IConnectionManager _connectionManager;
    private IDhtService _dhtService;
    private IConfigService _configService;
    private IPeerConnectionLogService _peerLogService;
    private NetworkController _subject;

    [SetUp]
    public void SetUp()
    {
        NetworkController.InvalidateDiagnosticsCache();

        _networkStatusService = Substitute.For<INetworkStatusService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _dhtService = Substitute.For<IDhtService>();
        _configService = Substitute.For<IConfigService>();
        _peerLogService = Substitute.For<IPeerConnectionLogService>();

        _peerLogService.GetConnectionCounts(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns((75, 25));

        _subject = new NetworkController(
            _networkStatusService,
            _connectionManager,
            _dhtService,
            _configService,
            _peerLogService);
    }

    [TearDown]
    public void TearDown()
    {
        NetworkController.InvalidateDiagnosticsCache();
    }

    [Test]
    public void GetDiagnostics_should_return_encrypted_and_plaintext_counts_and_percentage()
    {
        var result = _subject.GetDiagnostics();
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());

        var ok = (OkObjectResult)result.Result;
        var diag = (NetworkDiagnostics)ok.Value;

        Assert.That(diag.EncryptedConnections, Is.EqualTo(75));
        Assert.That(diag.PlaintextConnections, Is.EqualTo(25));
        Assert.That(diag.EncryptionPercentage, Is.EqualTo(75.0));
    }

    [Test]
    public void GetDiagnostics_should_cache_connection_counts_within_duration()
    {
        // First call populates cache
        var result1 = _subject.GetDiagnostics();
        var ok1 = (OkObjectResult)result1.Result;
        var diag1 = (NetworkDiagnostics)ok1.Value;

        // Change mock return value
        _peerLogService.GetConnectionCounts(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns((100, 0));

        // Second call within 30s should return cached counts
        var result2 = _subject.GetDiagnostics();
        var ok2 = (OkObjectResult)result2.Result;
        var diag2 = (NetworkDiagnostics)ok2.Value;

        Assert.That(diag2.EncryptedConnections, Is.EqualTo(75));
        Assert.That(diag2.PlaintextConnections, Is.EqualTo(25));
        _peerLogService.Received(1).GetConnectionCounts(Arg.Any<DateTime>(), Arg.Any<DateTime>());

        // Invalidate cache
        NetworkController.InvalidateDiagnosticsCache();

        // Third call should query again
        var result3 = _subject.GetDiagnostics();
        var ok3 = (OkObjectResult)result3.Result;
        var diag3 = (NetworkDiagnostics)ok3.Value;

        Assert.That(diag3.EncryptedConnections, Is.EqualTo(100));
        Assert.That(diag3.PlaintextConnections, Is.EqualTo(0));
        _peerLogService.Received(2).GetConnectionCounts(Arg.Any<DateTime>(), Arg.Any<DateTime>());
    }

    [Test]
    public void GetDiagnostics_should_handle_zero_connections_with_zero_percentage()
    {
        _peerLogService.GetConnectionCounts(Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns((0, 0));

        var result = _subject.GetDiagnostics();
        var ok = (OkObjectResult)result.Result;
        var diag = (NetworkDiagnostics)ok.Value;

        Assert.That(diag.EncryptedConnections, Is.EqualTo(0));
        Assert.That(diag.PlaintextConnections, Is.EqualTo(0));
        Assert.That(diag.EncryptionPercentage, Is.EqualTo(0));
    }
}
