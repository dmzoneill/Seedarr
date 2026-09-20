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

    [Test]
    public void SaveConfig_should_return_bad_request_when_MaxActiveDownloads_is_negative()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 20,
            UploadStoppedMaxPercentage = 40,
            DownloadStoppedMinPercentage = 20,
            DownloadStoppedMaxPercentage = 40,
            SpeedVariationMin = 0.2,
            SpeedVariationMax = 0.8,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
            MaxActiveDownloads = -1,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SeedingConfigResource.MaxActiveDownloads)));
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_MaxActiveSeeds_is_negative()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 20,
            UploadStoppedMaxPercentage = 40,
            DownloadStoppedMinPercentage = 20,
            DownloadStoppedMaxPercentage = 40,
            SpeedVariationMin = 0.2,
            SpeedVariationMax = 0.8,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
            MaxActiveSeeds = -1,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SeedingConfigResource.MaxActiveSeeds)));
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_MaxActiveTorrents_is_negative()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 20,
            UploadStoppedMaxPercentage = 40,
            DownloadStoppedMinPercentage = 20,
            DownloadStoppedMaxPercentage = 40,
            SpeedVariationMin = 0.2,
            SpeedVariationMax = 0.8,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
            MaxActiveTorrents = -1,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SeedingConfigResource.MaxActiveTorrents)));
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_SlowTorrentThresholdKbps_is_negative()
    {
        var resource = new SeedingConfigResource
        {
            UploadStoppedMinPercentage = 20,
            UploadStoppedMaxPercentage = 40,
            DownloadStoppedMinPercentage = 20,
            DownloadStoppedMaxPercentage = 40,
            SpeedVariationMin = 0.2,
            SpeedVariationMax = 0.8,
            UploadCustomIntervalMinutes = 5,
            DownloadCustomIntervalMinutes = 5,
            SlowTorrentThresholdKbps = -1,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(SeedingConfigResource.SlowTorrentThresholdKbps)));
    }

    [Test]
    public void GetConfig_should_map_queue_concurrency_properties()
    {
        _configService.MaxActiveDownloads.Returns(8);
        _configService.MaxActiveSeeds.Returns(15);
        _configService.MaxActiveTorrents.Returns(25);
        _configService.IgnoreSlowTorrents.Returns(false);
        _configService.SlowTorrentThresholdKbps.Returns(20);

        var resource = _controller.GetConfig();

        Assert.That(resource.MaxActiveDownloads, Is.EqualTo(8));
        Assert.That(resource.MaxActiveSeeds, Is.EqualTo(15));
        Assert.That(resource.MaxActiveTorrents, Is.EqualTo(25));
        Assert.That(resource.IgnoreSlowTorrents, Is.False);
        Assert.That(resource.SlowTorrentThresholdKbps, Is.EqualTo(20));
    }
}
