using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Developer.Repl;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperReplServiceTest
{
    private DeveloperReplService _replService;

    [SetUp]
    public void SetUp()
    {
        _replService = new DeveloperReplService();
    }

    [Test]
    public void Should_execute_basic_javascript_and_return_result()
    {
        var request = new ReplExecutionRequest
        {
            Code = "const a = 10; const b = 20; a + b;",
            Language = "javascript",
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("number"));
        Assert.That(response.ResultJson, Is.EqualTo("30"));
    }

    [Test]
    public void Should_capture_console_log_output()
    {
        var request = new ReplExecutionRequest
        {
            Code = "console.log('Hello', 'from', 'REPL');",
            Language = "javascript",
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.True);
        Assert.That(response.Output, Does.Contain("Hello from REPL"));
    }

    [Test]
    public void Should_record_and_retrieve_history()
    {
        _replService.ClearHistory();

        _replService.Execute(new ReplExecutionRequest { Code = "1 + 1" });
        _replService.Execute(new ReplExecutionRequest { Code = "2 + 2" });

        var history = _replService.GetHistory(10);

        Assert.That(history.Count, Is.EqualTo(2));
        Assert.That(history[0].Code, Is.EqualTo("2 + 2"));
        Assert.That(history[1].Code, Is.EqualTo("1 + 1"));

        _replService.ClearHistory();
        Assert.That(_replService.GetHistory(10), Is.Empty);
    }

    [Test]
    public void Should_reject_unsupported_language()
    {
        var request = new ReplExecutionRequest
        {
            Code = "print('hello')",
            Language = "python",
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.False);
        Assert.That(response.ResultType, Is.EqualTo("unsupported_language"));
        Assert.That(response.ErrorMessage, Does.Contain("not supported"));
    }

    [Test]
    public void Should_reset_session()
    {
        _replService.Execute(new ReplExecutionRequest { Code = "var myGlobal = 42;" });
        var check1 = _replService.Execute(new ReplExecutionRequest { Code = "myGlobal;" });
        Assert.That(check1.ResultJson, Is.EqualTo("42"));

        _replService.ResetSession();

        var check2 = _replService.Execute(new ReplExecutionRequest { Code = "typeof myGlobal;" });
        Assert.That(check2.ResultJson, Is.EqualTo("\"undefined\""));
    }

    [Test]
    public void Controller_should_evaluate_and_return_ok()
    {
        var controller = new Seedarr.Api.V1.System.SystemDeveloperReplController(_replService);
        var actionResult = controller.Evaluate(new ReplExecutionRequest { Code = "5 * 5" });

        var okResult = actionResult.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var response = okResult.Value as ReplExecutionResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultJson, Is.EqualTo("25"));
    }

    [Test]
    public void Controller_should_handle_history_and_reset()
    {
        var controller = new Seedarr.Api.V1.System.SystemDeveloperReplController(_replService);
        controller.Evaluate(new ReplExecutionRequest { Code = "10 + 20" });

        var historyResult = controller.GetHistory(10);
        var okResult = historyResult.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var clearResult = controller.ClearHistory();
        Assert.That(clearResult, Is.InstanceOf<Microsoft.AspNetCore.Mvc.NoContentResult>());

        var resetResult = controller.ResetSession();
        Assert.That(resetResult, Is.InstanceOf<Microsoft.AspNetCore.Mvc.NoContentResult>());
    }
}
