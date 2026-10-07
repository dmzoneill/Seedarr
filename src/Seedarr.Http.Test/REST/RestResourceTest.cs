using NUnit.Framework;
using Seedarr.Http.REST;

namespace Seedarr.Http.Test.REST;

public class IndirResource : RestResource
{
}

[TestFixture]
public class RestResourceTest
{
    [Test]
    [SetCulture("tr-TR")]
    public void ResourceName_uses_invariant_lowercase_for_signalr_wire_names()
    {
        var resource = new IndirResource();

        Assert.That(resource.ResourceName, Is.EqualTo("indir"));
        Assert.That(resource.ResourceName, Is.Not.EqualTo("ındir"));
    }

    [Test]
    public void ResourceName_strips_Resource_suffix()
    {
        var resource = new TestResource();

        Assert.That(resource.ResourceName, Is.EqualTo("test"));
    }
}
