using System;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Test.Exceptions;

[TestFixture]
public class DuplicateRemotePathMappingExceptionTest
{
    [Test]
    public void Constructor_sets_host_remote_path_and_default_message()
    {
        var ex = new DuplicateRemotePathMappingException("host1", "/remote");

        Assert.That(ex.Host, Is.EqualTo("host1"));
        Assert.That(ex.RemotePath, Is.EqualTo("/remote"));
        Assert.That(ex.Message, Does.Contain("host1").And.Contain("/remote"));
    }

    [Test]
    public void Constructor_with_custom_message_preserves_properties()
    {
        var ex = new DuplicateRemotePathMappingException("host2", "/path", "custom message");

        Assert.That(ex.Host, Is.EqualTo("host2"));
        Assert.That(ex.RemotePath, Is.EqualTo("/path"));
        Assert.That(ex.Message, Is.EqualTo("custom message"));
    }

    [Test]
    public void Constructor_with_inner_exception_uses_message()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new DuplicateRemotePathMappingException("outer", inner);

        Assert.That(ex.Message, Is.EqualTo("outer"));
        Assert.That(ex.InnerException, Is.SameAs(inner));
    }
}
