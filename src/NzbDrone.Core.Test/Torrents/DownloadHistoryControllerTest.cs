using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class DownloadHistoryControllerTest
{
    private IDownloadHistoryService _historyService;
    private IArrMetadataEnricherService _metadataEnricherService;
    private DownloadHistoryController _controller;

    [SetUp]
    public void SetUp()
    {
        _historyService = Substitute.For<IDownloadHistoryService>();
        _metadataEnricherService = Substitute.For<IArrMetadataEnricherService>();
        _controller = new DownloadHistoryController(_historyService, _metadataEnricherService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };
    }

    [TestCase("=1+1", "\"'=1+1\"")]
    [TestCase("+cmd", "\"'+cmd\"")]
    [TestCase("-calc", "\"'-calc\"")]
    [TestCase("@SUM(A1)", "\"'@SUM(A1)\"")]
    [TestCase("\tTab", "\"'\tTab\"")]
    [TestCase("\rReturn", "\"'\rReturn\"")]
    public void EscapeCsvField_prefixes_formula_trigger_characters_with_single_quote(string input, string expected)
    {
        var result = DownloadHistoryController.EscapeCsvField(input);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void EscapeCsvField_escapes_double_quotes()
    {
        var result = DownloadHistoryController.EscapeCsvField("Hello \"World\"");
        Assert.That(result, Is.EqualTo("\"Hello \"\"World\"\"\""));
    }

    [Test]
    public void EscapeCsvField_normal_text_wrapped_in_quotes()
    {
        var result = DownloadHistoryController.EscapeCsvField("Normal Title");
        Assert.That(result, Is.EqualTo("\"Normal Title\""));
    }

    [Test]
    public void EscapeCsvField_null_returns_empty_quotes()
    {
        var result = DownloadHistoryController.EscapeCsvField(null);
        Assert.That(result, Is.EqualTo("\"\""));
    }

    [Test]
    public void Export_when_format_is_json_returns_all_records_with_limit_minus_one()
    {
        var entries = new List<DownloadHistory>
        {
            new() { Id = 1, Title = "Movie 1", DateAdded = DateTime.UtcNow, Status = "Completed" },
            new() { Id = 2, Title = "Movie 2", DateAdded = DateTime.UtcNow, Status = "Seeding" }
        };

        _historyService.GetAll(null, null, -1, 0).Returns(entries);

        var result = _controller.Export(null, null, "json");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        var resources = okResult.Value as List<DownloadHistoryResource>;
        Assert.That(resources, Is.Not.Null);
        Assert.That(resources.Count, Is.EqualTo(2));
        Assert.That(resources[0].Title, Is.EqualTo("Movie 1"));
        Assert.That(resources[1].Title, Is.EqualTo("Movie 2"));
    }

    [Test]
    public void Export_when_format_is_csv_returns_file_result_with_escaped_csv()
    {
        var entries = new List<DownloadHistory>
        {
            new()
            {
                Id = 1,
                Title = "=cmd|' /C calc'!A0",
                InfoHash = "abc123hash",
                Source = "Prowlarr",
                Status = "Completed",
                TotalSize = 1048576,
                Uploaded = 2097152,
                Ratio = 2.0,
                SeedingTime = 3600,
                DateAdded = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                DateCompleted = new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc)
            }
        };

        _historyService.GetAll(null, null, -1, 0).Returns(entries);

        var result = _controller.Export(null, null, "csv");

        Assert.That(result, Is.InstanceOf<FileContentResult>());
        var fileResult = (FileContentResult)result;
        Assert.That(fileResult.ContentType, Is.EqualTo("text/csv; charset=utf-8"));
        Assert.That(fileResult.FileDownloadName, Does.StartWith("seedarr-history-"));

        var csvText = Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.That(csvText, Does.Contain("ID,Title,InfoHash,Source,Status"));
        // Check that = was escaped with single quote
        Assert.That(csvText, Does.Contain("\"'=cmd|' /C calc'!A0\""));
    }

    [Test]
    public void EnrichAll_executes_asynchronously_and_returns_immediate_status()
    {
        var result = _controller.EnrichAll();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());

        // Allow the Task.Run background worker to execute
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!_metadataEnricherService.ReceivedCalls().Any() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        _metadataEnricherService.Received(1).EnrichAll();
    }

    [Test]
    public void GetAll_passes_parameters_to_service()
    {
        var entries = new List<DownloadHistory>
        {
            new() { Id = 1, Title = "Filtered Item" }
        };

        _historyService.GetCount("search", "completed").Returns(1);
        _historyService.GetAll("search", "completed", 50, 10).Returns(entries);

        var result = _controller.GetAll("search", "completed", 50, 10);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _historyService.Received(1).GetAll("search", "completed", 50, 10);
        _historyService.Received(1).GetCount("search", "completed");
        Assert.That(_controller.Response.Headers["X-Total-Count"].ToString(), Is.EqualTo("1"));
        Assert.That(_controller.Response.Headers["X-Page-Count"].ToString(), Is.EqualTo("1"));
    }

    [Test]
    public void GetAll_with_page_and_pageSize_calculates_effective_limit_and_offset()
    {
        var entries = new List<DownloadHistory>
        {
            new() { Id = 10, Title = "Page 2 Item" }
        };

        _historyService.GetCount("linux", "Active").Returns(125);
        _historyService.GetAll("linux", "Active", 25, 25).Returns(entries);

        var result = _controller.GetAll("linux", "Active", page: 2, pageSize: 25);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _historyService.Received(1).GetAll("linux", "Active", 25, 25);
        _historyService.Received(1).GetCount("linux", "Active");
        Assert.That(_controller.Response.Headers["X-Total-Count"].ToString(), Is.EqualTo("125"));
        Assert.That(_controller.Response.Headers["X-Page-Count"].ToString(), Is.EqualTo("5"));
    }
}
