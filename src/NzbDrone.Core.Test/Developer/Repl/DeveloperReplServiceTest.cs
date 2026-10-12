using System;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Developer.Repl;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Developer.Repl;

[TestFixture]
public class DeveloperReplServiceTest
{
    private DeveloperReplService _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new DeveloperReplService();
    }

    [Test]
    public void Execute_null_request_throws()
    {
        Assert.That(() => _subject.Execute(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Execute_unsupported_language_returns_error()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Language = "python", Code = "1" });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorMessage, Does.Contain("not supported"));
    }

    [Test]
    public void Execute_empty_javascript_succeeds()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Language = "js", Code = "   " });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("undefined"));
    }

    [Test]
    public void Execute_simple_expression_returns_json_result()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Code = "1 + 2" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultJson, Does.Contain("3"));
    }

    [Test]
    public void Execute_console_log_captures_output_and_history()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Code = "console.log('hi', 2); 'done'" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.Output, Does.Contain("hi"));
        Assert.That(_subject.GetHistory(1), Has.Count.EqualTo(1));

        _subject.ClearHistory();
        Assert.That(_subject.GetHistory(), Is.Empty);
    }

    [Test]
    public void Execute_syntax_error_returns_error_result()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Code = "let x = ;" });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ResultType, Is.EqualTo("error"));
    }

    [Test]
    public void Execute_array_and_object_results_serialize()
    {
        var array = _subject.Execute(new ReplExecutionRequest { Code = "[1,2]" });
        var obj = _subject.Execute(new ReplExecutionRequest { Code = "({ a: 1 })" });

        Assert.That(array.Success, Is.True);
        Assert.That(array.ResultType, Is.EqualTo("array"));
        Assert.That(obj.Success, Is.True);
        Assert.That(obj.ResultType, Is.EqualTo("object"));
    }

    [Test]
    public void ResetSession_allows_fresh_execution()
    {
        _subject.Execute(new ReplExecutionRequest { Code = "var x = 1" });
        _subject.ResetSession();
        var response = _subject.Execute(new ReplExecutionRequest { Code = "typeof x" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("string"));
        Assert.That(response.ResultJson, Does.Contain("undefined"));
    }

    [Test]
    public void Execute_serializes_boolean_and_null_results()
    {
        var boolResult = _subject.Execute(new ReplExecutionRequest { Code = "true" });
        var nullResult = _subject.Execute(new ReplExecutionRequest { Code = "null" });

        Assert.That(boolResult.Success, Is.True);
        Assert.That(boolResult.ResultType, Is.EqualTo("boolean"));
        Assert.That(boolResult.ResultJson, Is.EqualTo("true"));

        Assert.That(nullResult.Success, Is.True);
        Assert.That(nullResult.ResultType, Is.EqualTo("null"));
        Assert.That(nullResult.ResultJson, Is.EqualTo("null"));
    }

    [Test]
    public void GetHistory_clamps_limit_to_valid_range()
    {
        for (var i = 0; i < 3; i++)
        {
            _subject.Execute(new ReplExecutionRequest { Code = $"{i}" });
        }

        Assert.That(_subject.GetHistory(0), Has.Count.EqualTo(1));
        Assert.That(_subject.GetHistory(2), Has.Count.EqualTo(2));
    }

    [Test]
    public void Execute_with_injected_services_exposes_host_objects_in_repl()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var seedingService = Substitute.For<ISeedingService>();
        var configService = Substitute.For<IConfigService>();
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        var mainDatabase = Substitute.For<IMainDatabase>();

        var subject = new DeveloperReplService(
            torrentService: torrentService,
            seedingService: seedingService,
            configService: configService,
            configFileProvider: configFileProvider,
            mainDatabase: mainDatabase);

        var torrentsType = subject.Execute(new ReplExecutionRequest { Code = "typeof torrents" });
        var engineType = subject.Execute(new ReplExecutionRequest { Code = "typeof engine" });
        var configType = subject.Execute(new ReplExecutionRequest { Code = "typeof config" });
        var dbType = subject.Execute(new ReplExecutionRequest { Code = "typeof db" });

        Assert.That(torrentsType.Success, Is.True);
        Assert.That(torrentsType.ResultJson, Does.Contain("object"));
        Assert.That(engineType.ResultJson, Does.Contain("object"));
        Assert.That(configType.ResultJson, Does.Contain("object"));
        Assert.That(dbType.ResultJson, Does.Contain("object"));
    }

    [Test]
    public void Execute_reinitializes_engine_when_timeout_changes()
    {
        var first = _subject.Execute(new ReplExecutionRequest { Code = "var marker = 1", TimeoutSeconds = 5 });
        var second = _subject.Execute(new ReplExecutionRequest { Code = "2 + 2", TimeoutSeconds = 30 });

        Assert.That(first.Success, Is.True);
        Assert.That(second.Success, Is.True);
        Assert.That(second.ResultJson, Does.Contain("4"));
    }

    [Test]
    public void Execute_serializes_function_and_number_results()
    {
        var number = _subject.Execute(new ReplExecutionRequest { Code = "3.5" });
        var functionType = _subject.Execute(new ReplExecutionRequest { Code = "typeof (function add(a,b) { return a + b; })" });

        Assert.That(number.Success, Is.True);
        Assert.That(number.ResultType, Is.EqualTo("number"));
        Assert.That(number.ResultJson, Does.Contain("3.5"));

        Assert.That(functionType.Success, Is.True);
        Assert.That(functionType.ResultType, Is.EqualTo("string"));
        Assert.That(functionType.ResultJson, Does.Contain("function"));
    }

    [Test]
    public void Execute_string_literal_result_serializes_as_json_string()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Code = "'hello repl'" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("string"));
        Assert.That(response.ResultJson, Does.Contain("hello repl"));
    }

    [Test]
    public void Execute_resolveService_is_available_without_provider()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Code = "typeof resolveService('missing')" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultJson, Does.Contain("object"));
    }

    [Test]
    public void Execute_returning_function_serializes_as_function_type()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Code = "(function add(a,b) { return a + b; })" });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("function"));
    }

    [Test]
    public void Execute_console_levels_and_empty_log_capture_output()
    {
        var response = _subject.Execute(new ReplExecutionRequest
        {
            Code = "console.log(); console.info('i'); console.warn('w'); console.error('e');",
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.Output, Does.Contain("[INFO] i"));
        Assert.That(response.Output, Does.Contain("[WARN] w"));
        Assert.That(response.Output, Does.Contain("[ERROR] e"));
    }

    [Test]
    public void Execute_resolveService_resolves_registered_service_by_name()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var services = new ServiceCollection();
        services.AddSingleton(torrentService);
        var provider = services.BuildServiceProvider();
        var subject = new DeveloperReplService(serviceProvider: provider);

        var response = subject.Execute(new ReplExecutionRequest
        {
            Code = "resolveService('ITorrentService') !== null && getService('ITorrentService') !== null",
        });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultType, Is.EqualTo("boolean"));
        Assert.That(response.ResultJson, Is.EqualTo("true"));
    }

    [Test]
    public void Execute_timeout_returns_timeout_result_and_resets_session()
    {
        var response = _subject.Execute(new ReplExecutionRequest
        {
            Code = "while (true) { }",
            TimeoutSeconds = 1,
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ResultType, Is.EqualTo("timeout"));
        Assert.That(response.ErrorMessage, Does.Contain("timed out").IgnoreCase);

        var afterReset = _subject.Execute(new ReplExecutionRequest { Code = "1 + 1", TimeoutSeconds = 5 });
        Assert.That(afterReset.Success, Is.True);
        Assert.That(afterReset.ResultJson, Does.Contain("2"));
    }

    [Test]
    public void Execute_clamps_timeout_seconds_to_supported_range()
    {
        var response = _subject.Execute(new ReplExecutionRequest { Code = "40 + 2", TimeoutSeconds = 120 });

        Assert.That(response.Success, Is.True);
        Assert.That(response.ResultJson, Does.Contain("42"));
    }

    [Test]
    public void GetHistory_returns_most_recent_entries_up_to_limit()
    {
        for (var i = 0; i < 5; i++)
        {
            _subject.Execute(new ReplExecutionRequest { Code = $"{i}" });
        }

        var history = _subject.GetHistory(3);
        Assert.That(history, Has.Count.EqualTo(3));
        Assert.That(history[0].Code, Is.EqualTo("4"));
    }
}
