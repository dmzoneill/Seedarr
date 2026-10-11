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
}
