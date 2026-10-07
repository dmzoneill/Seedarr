using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Common.Composition;

namespace NzbDrone.Core.Test.Composition;

[TestFixture]
public class AssemblyLoaderTest
{
    [Test]
    public void Load_returns_assemblies_when_all_names_resolve()
    {
        var assemblies = AssemblyLoader.Load(new List<string> { "Seedarr.Common" });

        Assert.That(assemblies, Has.Count.EqualTo(1));
        Assert.That(assemblies[0].GetName().Name, Is.EqualTo("Seedarr.Common"));
    }

    [Test]
    public void Load_reuses_default_context_assembly_when_dll_exists_beside_host()
    {
        var expected = typeof(AssemblyLoader).Assembly;
        var assemblies = AssemblyLoader.Load(new List<string> { expected.GetName().Name! });

        Assert.That(assemblies, Has.Count.EqualTo(1));
        Assert.That(assemblies[0], Is.SameAs(expected));
    }

    [Test]
    public void Load_throws_ArgumentNullException_when_names_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => AssemblyLoader.Load(null!));
    }

    [Test]
    public void Load_throws_HostStartupException_when_name_is_null()
    {
        var ex = Assert.Throws<HostStartupException>(() =>
            AssemblyLoader.Load(new List<string> { null! }));

        Assert.That(ex!.Message, Does.Contain("<null>"));
    }

    [Test]
    public void Load_throws_HostStartupException_when_name_is_empty_or_whitespace()
    {
        var empty = Assert.Throws<HostStartupException>(() =>
            AssemblyLoader.Load(new List<string> { string.Empty }));
        Assert.That(empty!.Message, Does.Contain("<empty>"));

        var whitespace = Assert.Throws<HostStartupException>(() =>
            AssemblyLoader.Load(new List<string> { "   " }));
        Assert.That(whitespace!.Message, Does.Contain("<whitespace>"));
    }

    [Test]
    public void Load_throws_HostStartupException_when_required_assembly_missing()
    {
        var ex = Assert.Throws<HostStartupException>(() =>
            AssemblyLoader.Load(new List<string> { "Seedarr.Missing.Assembly.ForTests" }));

        Assert.That(ex!.Message, Does.Contain("Seedarr.Missing.Assembly.ForTests"));
    }

    [Test]
    public void AutoAddServices_throws_when_assembly_list_includes_missing_module()
    {
        var container = new DryIoc.Container(rules => rules.WithNzbDroneRules());

        Assert.Throws<HostStartupException>(() =>
            container.AutoAddServices(new List<string> { "Seedarr.Missing.Assembly.ForTests" }));
    }
}
