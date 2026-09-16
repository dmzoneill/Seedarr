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
}
