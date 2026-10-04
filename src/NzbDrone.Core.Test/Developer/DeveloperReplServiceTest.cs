// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Developer.Repl;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.System;

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
    public void Should_throw_ArgumentNullException_when_request_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => _replService.Execute(null));
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

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\r\n\t  ")]
    public void Should_handle_empty_or_whitespace_code(string code)
    {
        var request = new ReplExecutionRequest
        {
            Code = code,
            Language = "javascript",
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("undefined"));
        Assert.That(response.ResultJson, Is.Null);
        Assert.That(response.Output, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Should_handle_javascript_syntax_error()
    {
        var request = new ReplExecutionRequest
        {
            Code = "invalid syntax !!@@",
            Language = "javascript",
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.False);
        Assert.That(response.ResultType, Is.EqualTo("error"));
        Assert.That(response.ErrorMessage, Does.Contain("JavaScript Error"));
        Assert.That(response.Output, Does.Contain("[ERROR]"));
    }

    [Test]
    public void Should_handle_javascript_runtime_exception()
    {
        var request = new ReplExecutionRequest
        {
            Code = "throw new Error('boom');",
            Language = "javascript",
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.False);
        Assert.That(response.ResultType, Is.EqualTo("error"));
        Assert.That(response.ErrorMessage, Does.Contain("boom"));
        Assert.That(response.Output, Does.Contain("[ERROR]"));
    }

    [TestCase("true", "true")]
    [TestCase("false", "false")]
    public void Should_evaluate_boolean_results(string code, string expectedJson)
    {
        var response = _replService.Execute(new ReplExecutionRequest { Code = code });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("boolean"));
        Assert.That(response.ResultJson, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Should_evaluate_null_result()
    {
        var response = _replService.Execute(new ReplExecutionRequest { Code = "null;" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("null"));
        Assert.That(response.ResultJson, Is.EqualTo("null"));
    }

    [Test]
    public void Should_evaluate_undefined_result()
    {
        var response = _replService.Execute(new ReplExecutionRequest { Code = "undefined;" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("undefined"));
        Assert.That(response.ResultJson, Is.Null);
    }

    [Test]
    public void Should_evaluate_string_result()
    {
        var response = _replService.Execute(new ReplExecutionRequest { Code = "'hello repl';" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("string"));
        Assert.That(response.ResultJson, Is.EqualTo("\"hello repl\""));
    }

    [Test]
    public void Should_evaluate_array_result()
    {
        var response = _replService.Execute(new ReplExecutionRequest { Code = "[1, 2, 'three'];" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("array"));
        Assert.That(response.ResultJson, Does.Contain("1"));
        Assert.That(response.ResultJson, Does.Contain("three"));
    }

    [Test]
    public void Should_evaluate_object_result()
    {
        var response = _replService.Execute(new ReplExecutionRequest { Code = "({ name: 'Seedarr', count: 42 });" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("object"));
        Assert.That(response.ResultJson, Does.Contain("Seedarr"));
        Assert.That(response.ResultJson, Does.Contain("42"));
    }

    [Test]
    public void Should_evaluate_function_result()
    {
        var response = _replService.Execute(new ReplExecutionRequest { Code = "(function testFn() { return 1; });" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("function"));
        Assert.That(response.ResultJson, Is.Not.Null);
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
    public void Should_support_all_console_logging_levels()
    {
        var request = new ReplExecutionRequest
        {
            Code = "console.info('info msg'); console.warn('warn msg'); console.error('error msg'); console.log();",
            Language = "javascript",
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.True);
        Assert.That(response.Output, Does.Contain("[INFO] info msg"));
        Assert.That(response.Output, Does.Contain("[WARN] warn msg"));
        Assert.That(response.Output, Does.Contain("[ERROR] error msg"));
    }

    [Test]
    public void Should_support_custom_timeout_parameter()
    {
        var request = new ReplExecutionRequest
        {
            Code = "2 + 3;",
            TimeoutSeconds = 5,
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultJson, Is.EqualTo("5"));
    }

    [Test]
    public void Should_timeout_when_script_execution_exceeds_timeout()
    {
        var request = new ReplExecutionRequest
        {
            Code = "while (true) {}",
            TimeoutSeconds = 1,
        };

        var response = _replService.Execute(request);

        Assert.That(response.Success, Is.False);
        Assert.That(response.ResultType, Is.EqualTo("timeout"));
        Assert.That(response.ErrorMessage, Does.Contain("timed out"));
        Assert.That(response.Output, Does.Contain("[ERROR]"));

        // Verify engine is reset after timeout and subsequent commands succeed
        var followup = _replService.Execute(new ReplExecutionRequest { Code = "10 + 20;" });
        Assert.That(followup.Success, Is.True);
        Assert.That(followup.ResultJson, Is.EqualTo("30"));
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
    public void Should_trim_history_when_exceeding_limit()
    {
        _replService.ClearHistory();

        for (var i = 0; i < 505; i++)
        {
            _replService.Execute(new ReplExecutionRequest { Code = $"var item{i} = {i};" });
        }

        var history = _replService.GetHistory(600);

        Assert.That(history.Count, Is.EqualTo(500));
        Assert.That(history[0].Code, Is.EqualTo("var item504 = 504;"));
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
    public void Should_inject_services_into_engine_when_provided()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var seedingService = Substitute.For<ISeedingService>();
        var configService = Substitute.For<IConfigService>();
        var mainDatabase = Substitute.For<IMainDatabase>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        serviceProvider.GetService(typeof(ITorrentService)).Returns(torrentService);

        var replWithServices = new DeveloperReplService(
            serviceProvider: serviceProvider,
            torrentService: torrentService,
            seedingService: seedingService,
            configService: configService,
            configFileProvider: null,
            mainDatabase: mainDatabase);

        var response = replWithServices.Execute(new ReplExecutionRequest
        {
            Code = "torrents !== undefined && engine !== undefined && config !== undefined && db !== undefined;",
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultJson, Is.EqualTo("true"));

        var resolveResponse = replWithServices.Execute(new ReplExecutionRequest
        {
            Code = "resolveService('ITorrentService') !== null;",
        });

        Assert.That(resolveResponse.Success, Is.True);
        Assert.That(resolveResponse.ResultJson, Is.EqualTo("true"));
    }

    [Test]
    public void Controller_should_evaluate_and_return_ok()
    {
        var controller = new SystemDeveloperReplController(_replService);
        var actionResult = controller.Evaluate(new ReplExecutionRequest { Code = "5 * 5" });

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var response = okResult.Value as ReplExecutionResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultJson, Is.EqualTo("25"));
    }

    [Test]
    public void Controller_should_handle_null_request_in_evaluate()
    {
        var controller = new SystemDeveloperReplController(_replService);
        var actionResult = controller.Evaluate(null);

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var response = okResult.Value as ReplExecutionResponse;
        Assert.That(response, Is.Not.Null);
        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("undefined"));
    }

    [Test]
    public void Controller_with_null_service_should_return_error_for_evaluate()
    {
        var controller = new SystemDeveloperReplController(null);
        var actionResult = controller.Evaluate(new ReplExecutionRequest { Code = "1 + 1" });

        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value.Success, Is.False);
        Assert.That(actionResult.Value.ErrorMessage, Does.Contain("not registered"));
    }

    [Test]
    public void Controller_with_null_service_should_return_empty_history()
    {
        var controller = new SystemDeveloperReplController(null);
        var actionResult = controller.GetHistory(10);

        Assert.That(actionResult.Value, Is.Not.Null);
        Assert.That(actionResult.Value, Is.Empty);
    }

    [Test]
    public void Controller_with_null_service_should_handle_clear_and_reset()
    {
        var controller = new SystemDeveloperReplController(null);

        var clearResult = controller.ClearHistory();
        Assert.That(clearResult, Is.InstanceOf<NoContentResult>());

        var resetResult = controller.ResetSession();
        Assert.That(resetResult, Is.InstanceOf<NoContentResult>());
    }

    [Test]
    public void Controller_should_handle_history_and_reset()
    {
        var controller = new SystemDeveloperReplController(_replService);
        controller.Evaluate(new ReplExecutionRequest { Code = "10 + 20" });

        var historyResult = controller.GetHistory(10);
        var okResult = historyResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var clearResult = controller.ClearHistory();
        Assert.That(clearResult, Is.InstanceOf<NoContentResult>());

        var resetResult = controller.ResetSession();
        Assert.That(resetResult, Is.InstanceOf<NoContentResult>());
    }
}
