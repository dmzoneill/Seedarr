using System;
using NUnit.Framework;
using NzbDrone.Core.Developer.Repl;

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
}
