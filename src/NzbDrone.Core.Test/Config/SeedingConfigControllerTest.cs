using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class SeedingConfigControllerTest
{
    private IConfigService _configService;
    private SeedingConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _controller = new SeedingConfigController(_configService);
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_UploadStoppedMaxPercentage_is_less_than_Min()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 60,
            UploadStoppedMaxPercentage = 20,
            DownloadStoppedMinPercentage = 20,
            DownloadStoppedMaxPercentage = 40,
            SpeedVariationMin = 0.2,
            SpeedVariationMax = 0.8,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SeedingConfigResource.UploadStoppedMaxPercentage) &&
            e.ErrorMessage.Contains("UploadStoppedMaxPercentage must be greater than or equal to UploadStoppedMinPercentage.")));
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_DownloadStoppedMaxPercentage_is_less_than_Min()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 20,
            UploadStoppedMaxPercentage = 40,
            DownloadStoppedMinPercentage = 70,
            DownloadStoppedMaxPercentage = 30,
            SpeedVariationMin = 0.2,
            SpeedVariationMax = 0.8,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SeedingConfigResource.DownloadStoppedMaxPercentage) &&
            e.ErrorMessage.Contains("DownloadStoppedMaxPercentage must be greater than or equal to DownloadStoppedMinPercentage.")));
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_SpeedVariationMax_is_less_than_Min()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 20,
            UploadStoppedMaxPercentage = 40,
            DownloadStoppedMinPercentage = 20,
            DownloadStoppedMaxPercentage = 40,
            SpeedVariationMin = 0.8,
            SpeedVariationMax = 0.3,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SeedingConfigResource.SpeedVariationMax) &&
            e.ErrorMessage.Contains("SpeedVariationMax must be greater than or equal to SpeedVariationMin.")));
    }

    [Test]
    public void SaveConfig_should_succeed_when_min_equals_max()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 30,
            UploadStoppedMaxPercentage = 30,
            DownloadStoppedMinPercentage = 25,
            DownloadStoppedMaxPercentage = 25,
            SpeedVariationMin = 0.5,
            SpeedVariationMax = 0.5,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_should_succeed_when_max_greater_than_min()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 20,
            UploadStoppedMaxPercentage = 50,
            DownloadStoppedMinPercentage = 10,
            DownloadStoppedMaxPercentage = 40,
            SpeedVariationMin = 0.2,
            SpeedVariationMax = 0.8,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }
}
