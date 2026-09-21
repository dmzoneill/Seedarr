using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class PortForwardCheckTest
{
    private IConfigService _configService;
    private IUpnpService _upnpService;
    private PortForwardCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _upnpService = Substitute.For<IUpnpService>();
        _subject = new PortForwardCheck(_configService, _upnpService);
    }

    [Test]
    public void Check_should_return_ok_when_config_is_null()
    {
        var check = new PortForwardCheck(null, null);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(result.Source, Is.EqualTo(nameof(PortForwardCheck)));
    }

    [Test]
    public void Check_should_return_ok_when_upnp_is_disabled()
    {
        _configService.UpnpEnabled.Returns(false);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_ok_when_peer_port_is_actively_mapped()
    {
        _configService.UpnpEnabled.Returns(true);
        _configService.ListeningPort.Returns(6881);

        _upnpService.IsAvailable.Returns(true);
        _upnpService.GetMappings().Returns(new List<PortMapping>
        {
            new PortMapping
            {
                InternalPort = 6881,
                ExternalPort = 6881,
                Protocol = "TCP",
                IsActive = true
            }
        });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_warning_when_upnp_service_is_null_or_unavailable()
    {
        _configService.UpnpEnabled.Returns(true);
        _configService.ListeningPort.Returns(6881);

        var checkWithNullService = new PortForwardCheck(_configService, null);
        var resultNull = checkWithNullService.Check();
        Assert.That(resultNull.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(resultNull.Message, Does.Contain("failed to map peer listening port 6881"));

        _upnpService.IsAvailable.Returns(false);
        _upnpService.GetMappings().Returns(new List<PortMapping>());
        var resultUnavailable = _subject.Check();
        Assert.That(resultUnavailable.Type, Is.EqualTo(HealthCheckResultType.Warning));
    }

    [Test]
    public void Check_should_return_warning_when_no_mapping_for_peer_port()
    {
        _configService.UpnpEnabled.Returns(true);
        _configService.ListeningPort.Returns(6881);

        _upnpService.IsAvailable.Returns(true);
        _upnpService.GetMappings().Returns(new List<PortMapping>
        {
            new PortMapping
            {
                InternalPort = 5000,
                ExternalPort = 5000,
                Protocol = "TCP",
                IsActive = true
            }
        });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("failed to map peer listening port 6881"));
    }

    [Test]
    public void Check_should_return_warning_when_mapping_is_inactive()
    {
        _configService.UpnpEnabled.Returns(true);
        _configService.ListeningPort.Returns(6881);

        _upnpService.IsAvailable.Returns(true);
        _upnpService.GetMappings().Returns(new List<PortMapping>
        {
            new PortMapping
            {
                InternalPort = 6881,
                ExternalPort = 6881,
                Protocol = "TCP",
                IsActive = false,
                ErrorMessage = "NAT mapping conflict"
            }
        });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("NAT mapping conflict"));
    }
}
