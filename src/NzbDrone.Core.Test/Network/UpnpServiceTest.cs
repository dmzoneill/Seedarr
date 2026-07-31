using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class UpnpServiceTest
{
    private IConfigService _configService;
    private IEventAggregator _eventAggregator;
    private UpnpService _subject;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _subject = new UpnpService(_configService, _eventAggregator);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _subject.StopAsync(CancellationToken.None);
        _subject.Dispose();
    }

    [Test]
    public void IsAvailable_should_default_to_false()
    {
        Assert.That(_subject.IsAvailable, Is.False);
    }

    [Test]
    public void ExternalIp_should_default_to_empty_string()
    {
        Assert.That(_subject.ExternalIp, Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetMappings_should_return_empty_list_initially()
    {
        var result = _subject.GetMappings();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetMappings_should_return_a_new_list_on_each_call()
    {
        var result1 = _subject.GetMappings();
        var result2 = _subject.GetMappings();

        Assert.That(result1, Is.Not.SameAs(result2));
    }

    [Test]
    public void GetMappings_should_return_list_of_port_mapping()
    {
        var result = _subject.GetMappings();

        Assert.That(result, Is.InstanceOf<List<PortMapping>>());
    }

    [Test]
    public async Task ExecuteAsync_should_return_immediately_when_upnp_disabled()
    {
        _configService.UpnpEnabled.Returns(false);

        await _subject.StartAsync(CancellationToken.None);

        // Give the background task time to run the disabled path (synchronous return)
        await Task.Delay(150);

        // No mapping events should fire when UPnP is disabled
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<UpnpMappingCreatedEvent>());
    }

    [Test]
    public void PortMapping_properties_should_be_settable()
    {
        var mapping = new PortMapping
        {
            InternalPort = 8080,
            ExternalPort = 9090,
            Protocol = "TCP",
            Description = "Test mapping",
            IsActive = true
        };

        Assert.That(mapping.InternalPort, Is.EqualTo(8080));
        Assert.That(mapping.ExternalPort, Is.EqualTo(9090));
        Assert.That(mapping.Protocol, Is.EqualTo("TCP"));
        Assert.That(mapping.Description, Is.EqualTo("Test mapping"));
        Assert.That(mapping.IsActive, Is.True);
    }

    [Test]
    public void PortMapping_should_have_false_IsActive_by_default()
    {
        var mapping = new PortMapping();

        Assert.That(mapping.IsActive, Is.False);
    }

    [Test]
    public void PortMapping_should_have_zero_ports_by_default()
    {
        var mapping = new PortMapping();

        Assert.That(mapping.InternalPort, Is.EqualTo(0));
        Assert.That(mapping.ExternalPort, Is.EqualTo(0));
    }

    [Test]
    public void PortMapping_protocol_and_description_should_be_null_by_default()
    {
        var mapping = new PortMapping();

        Assert.That(mapping.Protocol, Is.Null);
        Assert.That(mapping.Description, Is.Null);
    }

    [Test]
    public void UpnpMappingCreatedEvent_should_store_external_port()
    {
        var evt = new UpnpMappingCreatedEvent(6881);

        Assert.That(evt.ExternalPort, Is.EqualTo(6881));
    }

    [Test]
    public void UpnpMappingCreatedEvent_should_store_any_port_value()
    {
        var evt = new UpnpMappingCreatedEvent(9696);

        Assert.That(evt.ExternalPort, Is.EqualTo(9696));
    }

    [Test]
    public void UpnpMappingCreatedEvent_zero_port_should_be_stored()
    {
        var evt = new UpnpMappingCreatedEvent(0);

        Assert.That(evt.ExternalPort, Is.EqualTo(0));
    }
}
