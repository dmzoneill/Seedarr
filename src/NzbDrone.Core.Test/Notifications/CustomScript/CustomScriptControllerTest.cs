using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications;
using Seedarr.Api.V1.CustomScript;

namespace NzbDrone.Core.Test.Notifications.CustomScript;

[TestFixture]
public class CustomScriptControllerTest
{
    private ICustomScriptService _customScriptService;
    private IConfigService _configService;
    private CustomScriptController _controller;

    [SetUp]
    public void SetUp()
    {
        _customScriptService = Substitute.For<ICustomScriptService>();
        _configService = Substitute.For<IConfigService>();
        _controller = new CustomScriptController(_customScriptService, _configService);
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

    [Test]
    public void Controller_should_have_Authorize_AdminOnly_attribute()
    {
        var type = typeof(CustomScriptController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.AdminOnly));
    }

    [Test]
    public async Task TestScript_with_unauthorized_directory_should_reject()
    {
        var request = new CustomScriptTestRequest
        {
            ScriptPath = "/tmp/evil.sh",
            Arguments = "-c whoami",
        };

        var actionResult = await _controller.TestScript(request);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var testResult = okResult.Value as CustomScriptTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Stderr, Does.Contain("not permitted"));
        await _customScriptService.DidNotReceive().TestScriptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task TestScript_with_invalid_path_returns_failure_without_calling_service()
    {
        var request = new CustomScriptTestRequest { ScriptPath = "\0invalid" };

        var actionResult = await _controller.TestScript(request);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        var testResult = okResult.Value as CustomScriptTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Stderr, Does.StartWith("Invalid script path:"));
        await _customScriptService.DidNotReceive().TestScriptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task TestScript_uses_Test_event_type_when_event_type_blank()
    {
        _customScriptService.TestScriptAsync("/scripts/test.sh", null, "Test")
            .Returns(Task.FromResult(new CustomScriptTestResult { Success = true, ExitCode = 0 }));

        var request = new CustomScriptTestRequest
        {
            ScriptPath = "/scripts/test.sh",
            EventType = "   ",
        };

        var actionResult = await _controller.TestScript(request);
        var okResult = actionResult.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        await _customScriptService.Received(1).TestScriptAsync("/scripts/test.sh", null, "Test");
    }

    [Test]
    public void IsInAllowedDirectory_respects_custom_scripts_directory_from_config()
    {
        var customDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "seedarr-custom-scripts-test"));

        var inside = System.IO.Path.Combine(customDir, "run.sh");
        var outside = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "outside-run.sh");

        Assert.That(CustomScriptController.IsInAllowedDirectory(inside, customDir), Is.True);
        Assert.That(CustomScriptController.IsInAllowedDirectory(outside, customDir), Is.False);
    }
}
