using System.Collections.Generic;
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
