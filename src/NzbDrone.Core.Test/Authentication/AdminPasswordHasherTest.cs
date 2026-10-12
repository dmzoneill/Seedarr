using System;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class AdminPasswordHasherTest
{
    [Test]
    public void HashPassword_should_throw_for_null()
    {
        Assert.Throws<ArgumentNullException>(() => AdminPasswordHasher.HashPassword(null));
    }

    [Test]
    public void HashPassword_should_round_trip_through_verify()
    {
        var hash = AdminPasswordHasher.HashPassword("secret-value");
        Assert.That(hash, Does.StartWith("v1$"));
        Assert.That(AdminPasswordHasher.VerifyPassword("secret-value", hash), Is.True);
        Assert.That(AdminPasswordHasher.VerifyPassword("other", hash), Is.False);
    }

    [Test]
    public void VerifyPassword_should_reject_empty_inputs()
    {
        Assert.That(AdminPasswordHasher.VerifyPassword("", "v1$100000$x$y"), Is.False);
        Assert.That(AdminPasswordHasher.VerifyPassword("pw", ""), Is.False);
        Assert.That(AdminPasswordHasher.VerifyPassword(null, "plain"), Is.False);
    }

    [Test]
    public void VerifyPassword_should_use_fixed_time_equals_for_legacy_plaintext()
    {
        Assert.That(AdminPasswordHasher.VerifyPassword("legacy", "legacy"), Is.True);
        Assert.That(AdminPasswordHasher.VerifyPassword("legacy", "other"), Is.False);
    }

    [Test]
    public void VerifyPassword_should_reject_malformed_v1_payload()
    {
        Assert.That(AdminPasswordHasher.VerifyPassword("pw", "v1$0$salt$hash"), Is.False);
        Assert.That(AdminPasswordHasher.VerifyPassword("pw", "v1$100000$not-base64$hash"), Is.False);
        Assert.That(AdminPasswordHasher.VerifyPassword("pw", "v1$100000$AAAA$BBBB"), Is.False);
        Assert.That(AdminPasswordHasher.VerifyPassword("pw", "v1$only-three-parts"), Is.False);
    }

    [Test]
    public void FixedTimeEquals_should_handle_null_pairs()
    {
        Assert.That(AdminPasswordHasher.FixedTimeEquals(null, null), Is.True);
        Assert.That(AdminPasswordHasher.FixedTimeEquals("a", null), Is.False);
        Assert.That(AdminPasswordHasher.FixedTimeEquals(null, "b"), Is.False);
    }

    [Test]
    public void FixedTimeEquals_should_compare_hashes_without_timing_leaks()
    {
        Assert.That(AdminPasswordHasher.FixedTimeEquals("same", "same"), Is.True);
        Assert.That(AdminPasswordHasher.FixedTimeEquals("a", "b"), Is.False);
    }
}
