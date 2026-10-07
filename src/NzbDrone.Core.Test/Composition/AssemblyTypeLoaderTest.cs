using NUnit.Framework;
using NzbDrone.Common.Composition;

namespace NzbDrone.Core.Test.Composition;

[TestFixture]
public class AssemblyTypeLoaderTest
{
    [Test]
    public void GetExportedTypes_includes_known_type_from_assembly()
    {
        var types = AssemblyTypeLoader.GetExportedTypes(typeof(ContainerExtensions).Assembly);

        Assert.That(types, Does.Contain(typeof(ContainerExtensions)));
        Assert.That(types, Does.Contain(typeof(AssemblyTypeLoader)));
    }
}
