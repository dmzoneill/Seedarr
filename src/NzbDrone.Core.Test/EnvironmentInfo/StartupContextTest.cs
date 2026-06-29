using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Test.EnvironmentInfo;

[TestFixture]
public class StartupContextTest
{
    [Test]
    public void Constructor_with_no_args_produces_empty_flags_and_args()
    {
        var subject = new StartupContext();

        Assert.That(subject.Flags, Is.Empty);
        Assert.That(subject.Args, Is.Empty);
    }

    [Test]
    public void Flag_with_double_dash_prefix_is_added_to_flags()
    {
        var subject = new StartupContext("--nobrowser");

        Assert.That(subject.Flags, Contains.Item("nobrowser"));
    }

    [Test]
    public void Flag_with_forward_slash_prefix_is_added_to_flags()
    {
        var subject = new StartupContext("/nobrowser");

        Assert.That(subject.Flags, Contains.Item("nobrowser"));
    }

    [Test]
    public void Flag_is_stored_lowercase()
    {
        var subject = new StartupContext("--NoBrowser");

        Assert.That(subject.Flags, Contains.Item("nobrowser"));
        Assert.That(subject.Flags, Does.Not.Contain("NoBrowser"));
    }

    [Test]
    public void Key_value_arg_with_double_dash_is_added_to_args()
    {
        var subject = new StartupContext("--data=/path/to/data");

        Assert.That(subject.Args.ContainsKey("data"), Is.True);
        Assert.That(subject.Args["data"], Is.EqualTo("/path/to/data"));
    }

    [Test]
    public void Key_value_arg_with_forward_slash_is_added_to_args()
    {
        var subject = new StartupContext("/data=/path/to/data");

        Assert.That(subject.Args.ContainsKey("data"), Is.True);
        Assert.That(subject.Args["data"], Is.EqualTo("/path/to/data"));
    }

    [Test]
    public void Arg_key_is_stored_lowercase()
    {
        var subject = new StartupContext("--Data=/path");

        Assert.That(subject.Args.ContainsKey("data"), Is.True);
    }

    [Test]
    public void Args_dictionary_is_case_insensitive()
    {
        var subject = new StartupContext("--data=/path");

        Assert.That(subject.Args.ContainsKey("DATA"), Is.True);
        Assert.That(subject.Args.ContainsKey("Data"), Is.True);
    }

    [Test]
    public void Value_containing_equals_sign_is_preserved()
    {
        var subject = new StartupContext("--key=val=ue");

        Assert.That(subject.Args["key"], Is.EqualTo("val=ue"));
    }

    [Test]
    public void Multiple_flags_and_args_are_all_parsed()
    {
        var subject = new StartupContext("--nobrowser", "--data=/tmp/seedarr", "--port=8080");

        Assert.That(subject.Flags, Contains.Item("nobrowser"));
        Assert.That(subject.Args["data"], Is.EqualTo("/tmp/seedarr"));
        Assert.That(subject.Args["port"], Is.EqualTo("8080"));
    }

    [Test]
    public void Flag_is_not_added_to_args()
    {
        var subject = new StartupContext("--nobrowser");

        Assert.That(subject.Args, Is.Empty);
    }

    [Test]
    public void Key_value_arg_is_not_added_to_flags()
    {
        var subject = new StartupContext("--data=/path");

        Assert.That(subject.Flags, Is.Empty);
    }

    [Test]
    public void Single_dash_flag_is_parsed()
    {
        var subject = new StartupContext("-nobrowser");

        Assert.That(subject.Flags, Contains.Item("nobrowser"));
    }
}
