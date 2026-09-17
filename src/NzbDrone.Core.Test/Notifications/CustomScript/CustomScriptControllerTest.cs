using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using Seedarr.Api.V1.CustomScript;

namespace NzbDrone.Core.Test.Notifications.CustomScript;

[TestFixture]
public class CustomScriptControllerTest
{
    private ICustomScriptService _customScriptService;
    private CustomScriptController _controller;

    [SetUp]
    public void SetUp()
    {
        _customScriptService = Substitute.For<ICustomScriptService>();
        _controller = new CustomScriptController(_customScriptService);
    }

    [Test]
    public async Task TestScript_with_null_request_returns_failure()
    {
        var actionResult = await _controller.TestScript(null);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var testResult = okResult.Value as CustomScriptTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.ExitCode, Is.EqualTo(-1));
        Assert.That(testResult.Stderr, Does.Contain("Script path is required"));
    }

    [Test]
    public async Task TestScript_with_empty_path_returns_failure()
    {
        var actionResult = await _controller.TestScript(new CustomScriptTestRequest { ScriptPath = "   " });
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var testResult = okResult.Value as CustomScriptTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.ExitCode, Is.EqualTo(-1));
        Assert.That(testResult.Stderr, Does.Contain("Script path is required"));
    }

    [Test]
    public async Task TestScript_delegates_to_service_and_returns_result()
    {
        var expectedResult = new CustomScriptTestResult
        {
            Success = true,
            ExitCode = 0,
            Stdout = "Script executed successfully",
            Stderr = string.Empty,
            ExecutionTimeMs = 45,
            TimedOut = false,
            ResolvedInterpreter = "/bin/sh",
            WorkingDirectory = "/scripts",
        };

        _customScriptService.TestScriptAsync("/scripts/test.sh", "--arg1 value", "OnDownloadComplete")
            .Returns(Task.FromResult(expectedResult));

        var request = new CustomScriptTestRequest
        {
            ScriptPath = "/scripts/test.sh",
            Arguments = "--arg1 value",
            EventType = "OnDownloadComplete",
        };

        var actionResult = await _controller.TestScript(request);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var testResult = okResult.Value as CustomScriptTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.True);
        Assert.That(testResult.ExitCode, Is.EqualTo(0));
        Assert.That(testResult.Stdout, Is.EqualTo("Script executed successfully"));
        Assert.That(testResult.ExecutionTimeMs, Is.EqualTo(45));
    }
}
