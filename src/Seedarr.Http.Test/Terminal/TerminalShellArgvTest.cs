// Copyright (c) FeedItOut. All rights reserved.

using NUnit.Framework;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.Terminal;

[TestFixture]
public class TerminalShellArgvTest
{
    [Test]
    public void BuildInteractiveExecArgv_bash_skips_profile_and_rc()
    {
        Assert.That(
            TerminalShellArgv.BuildInteractiveExecArgv("/bin/bash"),
            Is.EqualTo(new[] { "/bin/bash", "--noprofile", "--norc", "-i" }));
    }

    [Test]
    public void BuildInteractiveExecArgv_sh_stays_non_interactive_profile_wise()
    {
        Assert.That(
            TerminalShellArgv.BuildInteractiveExecArgv("/bin/sh"),
            Is.EqualTo(new[] { "/bin/sh", "-i" }));
    }

    [Test]
    public void BuildInteractiveProcessArguments_matches_exec_argv_tail()
    {
        Assert.That(
            TerminalShellArgv.BuildInteractiveProcessArguments("/bin/bash"),
            Is.EqualTo("--noprofile --norc -i"));
    }
}
