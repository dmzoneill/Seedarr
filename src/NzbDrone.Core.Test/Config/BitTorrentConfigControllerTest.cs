using System;
using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class BitTorrentConfigControllerTest
{
    private IConfigService _configService;
    private BitTorrentConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _controller = new BitTorrentConfigController(_configService);
    }

    [TestCase("bogus")]
    [TestCase("invalid")]
    [TestCase("")]
    [TestCase(null)]
    public void SaveConfig_should_return_bad_request_when_EncryptionMode_is_invalid(string invalidMode)
    {
        var resource = new BitTorrentConfigResource
        {
            EnableDht = true,
            EnablePex = true,
            EnableLpd = true,
            EncryptionMode = invalidMode,
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 300,
            ScrapeIntervalSeconds = 900,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(BitTorrentConfigResource.EncryptionMode) &&
            e.ErrorMessage.Contains("EncryptionMode must be one of: disabled, enabled, required, forced.")));
    }

    [TestCase("disabled")]
    [TestCase("enabled")]
    [TestCase("required")]
    [TestCase("forced")]
    [TestCase("FORCED")]
    [TestCase("Required")]
    public void SaveConfig_should_succeed_for_valid_encryption_modes(string validMode)
    {
        var resource = new BitTorrentConfigResource
        {
            EnableDht = true,
            EnablePex = true,
            EnableLpd = true,
            EncryptionMode = validMode,
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 300,
            ScrapeIntervalSeconds = 900,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(-60)]
    [TestCase(4)]
    [TestCase(3601)]
    [TestCase(100000)]
    public void SaveConfig_should_return_bad_request_when_CustomScriptTimeoutSeconds_is_out_of_range(int invalidTimeout)
    {
        var resource = new BitTorrentConfigResource
        {
            EnableDht = true,
            EnablePex = true,
            EnableLpd = true,
            EncryptionMode = "enabled",
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 300,
            ScrapeIntervalSeconds = 900,
            CustomScriptTimeoutSeconds = invalidTimeout,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(BitTorrentConfigResource.CustomScriptTimeoutSeconds) &&
            e.ErrorMessage.Contains("between 5 and 3600 seconds")));
    }

    [TestCase("1234567890123")]
    [TestCase("-qB4420-123456")]
    public void SaveConfig_should_return_bad_request_when_PeerIdPrefix_exceeds_12_characters(string invalidPrefix)
    {
        var resource = new BitTorrentConfigResource
        {
            EnableDht = true,
            EnablePex = true,
            EnableLpd = true,
            EncryptionMode = "enabled",
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 300,
            ScrapeIntervalSeconds = 900,
            PeerIdPrefix = invalidPrefix,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(BitTorrentConfigResource.PeerIdPrefix) &&
            e.ErrorMessage.Contains("cannot exceed 12 characters")));
    }

    [TestCase("-qB4420-")]
    [TestCase("123456789012")]
    [TestCase("")]
    [TestCase(null)]
    public void SaveConfig_should_succeed_for_valid_PeerIdPrefix(string validPrefix)
    {
        var resource = new BitTorrentConfigResource
        {
            EnableDht = true,
            EnablePex = true,
            EnableLpd = true,
            EncryptionMode = "enabled",
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 300,
            ScrapeIntervalSeconds = 900,
            PeerIdPrefix = validPrefix,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_should_return_problem_when_config_service_throws()
    {
        _configService.When(x => x.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>()))
            .Do(_ => throw new InvalidOperationException("Database locked"));

        var resource = new BitTorrentConfigResource
        {
            EnableDht = true,
            EnablePex = true,
            EnableLpd = true,
            EncryptionMode = "enabled",
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 300,
            ScrapeIntervalSeconds = 900,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result.Result;
        Assert.That(objResult.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public void SaveConfig_should_reject_mismatched_route_id()
    {
        var resource = CreateValidResource();

        var result = _controller.SaveConfig(999, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void SaveConfig_should_accept_canonical_route_id()
    {
        var resource = CreateValidResource();
        resource.Id = 1;

        var result = _controller.SaveConfig(1, resource);

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_should_reject_body_id_other_than_one()
    {
        var resource = CreateValidResource();
        resource.Id = 999;

        var result = _controller.SaveConfig(null, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _configService.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    private static BitTorrentConfigResource CreateValidResource()
    {
        return new BitTorrentConfigResource
        {
            EnableDht = true,
            EnablePex = true,
            EnableLpd = true,
            EncryptionMode = "enabled",
            AnnounceIntervalSeconds = 1800,
            MinAnnounceIntervalSeconds = 300,
            ScrapeIntervalSeconds = 900,
        };
    }
}
