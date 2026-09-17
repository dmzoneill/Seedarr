using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using Open.Nat;

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

    [Test]
    public void PortMapping_ErrorMessage_should_be_null_by_default()
    {
        var mapping = new PortMapping();

        Assert.That(mapping.ErrorMessage, Is.Null);
    }

    [Test]
    public void PortMapping_ErrorMessage_should_be_settable()
    {
        var mapping = new PortMapping
        {
            ErrorMessage = "Test error"
        };

        Assert.That(mapping.ErrorMessage, Is.EqualTo("Test error"));
    }

    [Test]
    public async Task CreateMappings_should_not_publish_event_if_peer_port_mapping_fails()
    {
        _configService.ListeningPort.Returns(6881);
        _configService.TrackerHttpPort.Returns(9696);

        var mockDevice = Substitute.For<IUpnpDevice>();
        mockDevice.GetExternalIPAsync().Returns(IPAddress.Parse("203.0.113.1"));
        mockDevice.CreatePortMapAsync(Arg.Is<Mapping>(m => m.PublicPort == 6881 && m.Protocol == Protocol.Tcp))
            .ThrowsAsync(new Exception("Port map failed"));

        var service = new UpnpService(_configService, _eventAggregator, _ => Task.FromResult(mockDevice));
        await service.CreateMappings(CancellationToken.None);

        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<UpnpMappingCreatedEvent>());
    }

    [Test]
    public async Task CreateMappings_should_set_IsActive_false_and_record_error_on_failure()
    {
        _configService.ListeningPort.Returns(6881);
        _configService.TrackerHttpPort.Returns(9696);

        var mockDevice = Substitute.For<IUpnpDevice>();
        mockDevice.GetExternalIPAsync().Returns(IPAddress.Parse("203.0.113.1"));
        mockDevice.CreatePortMapAsync(Arg.Any<Mapping>())
            .ThrowsAsync(new Exception("Gateway error"));

        var service = new UpnpService(_configService, _eventAggregator, _ => Task.FromResult(mockDevice));
        await service.CreateMappings(CancellationToken.None);

        var mappings = service.GetMappings();
        Assert.That(mappings, Is.Not.Empty);
        Assert.That(mappings.All(m => !m.IsActive), Is.True);
        Assert.That(mappings[0].ErrorMessage, Does.Contain("Gateway error"));
    }

    [Test]
    public async Task CreateMappings_should_set_IsActive_true_and_publish_event_on_success()
    {
        _configService.ListeningPort.Returns(6881);
        _configService.TrackerHttpPort.Returns(9696);

        var mockDevice = Substitute.For<IUpnpDevice>();
        mockDevice.GetExternalIPAsync().Returns(IPAddress.Parse("203.0.113.1"));
        mockDevice.CreatePortMapAsync(Arg.Any<Mapping>()).Returns(Task.CompletedTask);

        var service = new UpnpService(_configService, _eventAggregator, _ => Task.FromResult(mockDevice));
        await service.CreateMappings(CancellationToken.None);

        _eventAggregator.Received(1).PublishEvent(Arg.Is<UpnpMappingCreatedEvent>(e => e.ExternalPort == 6881));
        var mappings = service.GetMappings();
        Assert.That(mappings, Is.Not.Empty);
        Assert.That(mappings.All(m => m.IsActive), Is.True);
        Assert.That(mappings.All(m => m.ErrorMessage == null), Is.True);
        Assert.That(service.ExternalIp, Is.EqualTo("203.0.113.1"));
        Assert.That(service.IsAvailable, Is.True);
    }

    [Test]
    public async Task CreateMappings_should_detect_port_conflict_code_718()
    {
        _configService.ListeningPort.Returns(6881);
        _configService.TrackerHttpPort.Returns(9696);

        var mockDevice = Substitute.For<IUpnpDevice>();
        mockDevice.GetExternalIPAsync().Returns(IPAddress.Parse("203.0.113.1"));
        mockDevice.CreatePortMapAsync(Arg.Any<Mapping>())
            .ThrowsAsync(CreateMappingException(718, "ConflictInMappingEntry"));

        var service = new UpnpService(_configService, _eventAggregator, _ => Task.FromResult(mockDevice));
        await service.CreateMappings(CancellationToken.None);

        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<UpnpMappingCreatedEvent>());
        var mappings = service.GetMappings();
        Assert.That(mappings, Is.Not.Empty);
        Assert.That(mappings[0].IsActive, Is.False);
        Assert.That(mappings[0].ErrorMessage, Does.Contain("718").And.Contain("Conflict"));
    }

    [Test]
    public async Task RemoveMappings_should_reuse_cached_device_without_invoking_deviceDiscoverer_again()
    {
        _configService.ListeningPort.Returns(6881);
        _configService.TrackerHttpPort.Returns(9696);

        var mockDevice = Substitute.For<IUpnpDevice>();
        mockDevice.GetExternalIPAsync().Returns(IPAddress.Parse("203.0.113.1"));
        mockDevice.CreatePortMapAsync(Arg.Any<Mapping>()).Returns(Task.CompletedTask);
        mockDevice.DeletePortMapAsync(Arg.Any<Mapping>()).Returns(Task.CompletedTask);

        var discovererCallCount = 0;
        Func<CancellationToken, Task<IUpnpDevice>> discoverer = _ =>
        {
            discovererCallCount++;
            return Task.FromResult(mockDevice);
        };

        var service = new UpnpService(_configService, _eventAggregator, discoverer);
        await service.CreateMappings(CancellationToken.None);

        Assert.That(discovererCallCount, Is.EqualTo(1));

        await service.RemoveMappings();

        Assert.That(discovererCallCount, Is.EqualTo(1));
        await mockDevice.Received().DeletePortMapAsync(Arg.Any<Mapping>());
        Assert.That(service.IsAvailable, Is.False);
    }

    [Test]
    public async Task RemoveMappings_should_not_attempt_device_discovery_when_no_device_discovered()
    {
        var discovererCalled = false;
        Func<CancellationToken, Task<IUpnpDevice>> discoverer = _ =>
        {
            discovererCalled = true;
            return Task.FromResult(Substitute.For<IUpnpDevice>());
        };

        var service = new UpnpService(_configService, _eventAggregator, discoverer);

        await service.RemoveMappings();

        Assert.That(discovererCalled, Is.False);
        Assert.That(service.IsAvailable, Is.False);
    }

    [Test]
    public async Task RemoveMappings_should_abort_gracefully_when_mapping_deletion_times_out_or_is_cancelled()
    {
        _configService.ListeningPort.Returns(6881);
        _configService.TrackerHttpPort.Returns(9696);

        var mockDevice = Substitute.For<IUpnpDevice>();
        mockDevice.GetExternalIPAsync().Returns(IPAddress.Parse("203.0.113.1"));
        mockDevice.CreatePortMapAsync(Arg.Any<Mapping>()).Returns(Task.CompletedTask);
        mockDevice.DeletePortMapAsync(Arg.Any<Mapping>()).Returns(async _ =>
        {
            await Task.Delay(10000);
        });

        var service = new UpnpService(_configService, _eventAggregator, _ => Task.FromResult(mockDevice));
        await service.CreateMappings(CancellationToken.None);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.DoesNotThrowAsync(async () => await service.RemoveMappings());
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(4000));
        Assert.That(service.IsAvailable, Is.False);
    }

    private static MappingException CreateMappingException(int errorCode, string errorText)
    {
        var ctor = typeof(MappingException).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof(int), typeof(string) },
            null);

        if (ctor != null)
        {
            return (MappingException)ctor.Invoke(new object[] { errorCode, errorText });
        }

        var ex = (MappingException)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MappingException));
        var prop = typeof(MappingException).GetProperty("ErrorCode");
        prop?.SetValue(ex, errorCode);
        return ex;
    }
}
