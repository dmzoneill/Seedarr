using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class NetworkConfigControllerTest
{
    private IConfigService _configService;
    private NetworkConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _controller = new NetworkConfigController(_configService);
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_MaxPerTorrentConnections_exceeds_MaxGlobalConnections()
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 50,
            MaxPerTorrentConnections = 200,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(NetworkConfigResource.MaxPerTorrentConnections) &&
            e.ErrorMessage.Contains("Max Connections Per Torrent cannot exceed Maximum Global Connections.")));
    }

    [Test]
    public void SaveConfig_should_succeed_when_MaxPerTorrentConnections_is_less_than_or_equal_to_MaxGlobalConnections()
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_with_new_proxy_password_containing_asterisks_saves_password()
    {
        _configService.ProxyPassword.Returns("OldProxyPass123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = "*Proxy*Pass*",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("*Proxy*Pass*"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "*Proxy*Pass*"));
    }

    [Test]
    public void SaveConfig_with_exact_mask_preserves_existing_proxy_password()
    {
        _configService.ProxyPassword.Returns("ExistingProxySecret123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = "********",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("ExistingProxySecret123"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "ExistingProxySecret123"));
    }

    [Test]
    public void SaveConfig_with_unchanged_sentinel_preserves_existing_proxy_password()
    {
        _configService.ProxyPassword.Returns("ExistingProxySecret123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = "(unchanged)",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("ExistingProxySecret123"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "ExistingProxySecret123"));
    }

    [Test]
    public void SaveConfig_with_null_preserves_existing_proxy_password()
    {
        _configService.ProxyPassword.Returns("ExistingProxySecret123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = null,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("ExistingProxySecret123"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "ExistingProxySecret123"));
    }
}
