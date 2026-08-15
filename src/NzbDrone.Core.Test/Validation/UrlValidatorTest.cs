using NUnit.Framework;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.Validation;

[TestFixture]
public class UrlValidatorTest
{
    [Test]
    public void IsSafeUrl_should_return_false_for_null()
    {
        Assert.That(UrlValidator.IsSafeUrl(null), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_empty_string()
    {
        Assert.That(UrlValidator.IsSafeUrl(""), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_whitespace()
    {
        Assert.That(UrlValidator.IsSafeUrl("   "), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_relative_uri()
    {
        Assert.That(UrlValidator.IsSafeUrl("/relative/path"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_ftp_scheme()
    {
        Assert.That(UrlValidator.IsSafeUrl("ftp://example.com/file"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_file_scheme()
    {
        Assert.That(UrlValidator.IsSafeUrl("file:///etc/passwd"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_loopback_127_0_0_1()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://127.0.0.1/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_loopback_127_0_0_2()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://127.0.0.2/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_10_x_x_x()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://10.0.0.1/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_172_16_x_x()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://172.16.0.1/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_172_31_x_x()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://172.31.255.255/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_192_168_x_x()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://192.168.1.1/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_169_254_x_x()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://169.254.1.1/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_true_for_public_ip_http()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://8.8.8.8/api"), Is.True);
    }

    [Test]
    public void IsSafeUrl_should_return_true_for_public_ip_https()
    {
        Assert.That(UrlValidator.IsSafeUrl("https://8.8.4.4/api"), Is.True);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_ipv6_loopback()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://[::1]/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_unresolvable_hostname()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://this-host-definitely-does-not-exist-xyz123.invalid/api"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_true_for_172_32_0_1()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://172.32.0.1/api"), Is.True);
    }

    [Test]
    public void IsSafeUrl_should_return_true_for_172_15_0_1()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://172.15.0.1/api"), Is.True);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_javascript_scheme()
    {
        Assert.That(UrlValidator.IsSafeUrl("javascript:alert(1)"), Is.False);
    }

    [Test]
    public void IsSafeUrl_should_return_false_for_10_255_255_255()
    {
        Assert.That(UrlValidator.IsSafeUrl("http://10.255.255.255/api"), Is.False);
    }
}
