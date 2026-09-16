using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Seedarr.Http.Security;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class RpcSessionStoreTest
{
    private RpcSessionStore _store;

    [SetUp]
    public void SetUp()
    {
        _store = new RpcSessionStore(100);
    }

    [Test]
    public void IsValid_ReturnsTrueForValidSession_AndFalseForExpiredOrNonExistent()
    {
        _store.SetSession("valid_token", DateTime.UtcNow.AddHours(1));
        _store.SetSession("expired_token", DateTime.UtcNow.AddHours(-1));
        _store.SetSession("lifetime_token", TimeSpan.FromMinutes(30));

        Assert.That(_store.IsValid("valid_token"), Is.True);
        Assert.That(_store.IsValid("expired_token"), Is.False);
        Assert.That(_store.IsValid("lifetime_token"), Is.True);
        Assert.That(_store.IsValid("nonexistent"), Is.False);
        Assert.That(_store.IsValid(null), Is.False);
        Assert.That(_store.IsValid(string.Empty), Is.False);
    }

    [Test]
    public void RemoveSession_RemovesTokenSuccessfully()
    {
        _store.SetSession("test_token", TimeSpan.FromHours(1));
        Assert.That(_store.IsValid("test_token"), Is.True);

        var removed = _store.RemoveSession("test_token");
        Assert.That(removed, Is.True);
        Assert.That(_store.IsValid("test_token"), Is.False);

        var removedAgain = _store.RemoveSession("test_token");
        Assert.That(removedAgain, Is.False);
    }

    [Test]
    public void TryGetValue_ReturnsExpiryForActiveSession()
    {
        var expiry = DateTime.UtcNow.AddHours(2);
        _store.SetSession("test_token", expiry);

        var found = _store.TryGetValue("test_token", out var retrievedExpiry);
        Assert.That(found, Is.True);
        Assert.That(retrievedExpiry, Is.EqualTo(expiry));

        var foundNonexistent = _store.TryGetValue("unknown", out _);
        Assert.That(foundNonexistent, Is.False);
    }

    [Test]
    public void PruneExpired_RemovesOnlyExpiredSessions()
    {
        _store.SetSession("valid1", DateTime.UtcNow.AddHours(1));
        _store.SetSession("valid2", DateTime.UtcNow.AddHours(2));
        _store.SetSession("expired1", DateTime.UtcNow.AddSeconds(-10));
        _store.SetSession("expired2", DateTime.UtcNow.AddMinutes(-5));

        Assert.That(_store.Count, Is.EqualTo(4));

        _store.PruneExpired();

        Assert.That(_store.Count, Is.EqualTo(2));
        Assert.That(_store.IsValid("valid1"), Is.True);
        Assert.That(_store.IsValid("valid2"), Is.True);
        Assert.That(_store.IsValid("expired1"), Is.False);
        Assert.That(_store.IsValid("expired2"), Is.False);
    }

    [Test]
    public void CapacityEnforcement_EvictsExcessEntries_WhenCapacityExceeded()
    {
        var smallStore = new RpcSessionStore(10);

        for (var i = 1; i <= 20; i++)
        {
            // First 5 are already expired, rest are valid into the future with increasing expiry
            var expiry = i <= 5 ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddHours(i);
            smallStore.SetSession($"token_{i}", expiry);
        }

        Assert.That(smallStore.Count, Is.LessThanOrEqualTo(10));

        // The latest tokens (15..20) should definitely be valid
        for (var i = 15; i <= 20; i++)
        {
            Assert.That(smallStore.IsValid($"token_{i}"), Is.True);
        }
    }

    [Test]
    public void Clear_And_InvalidateAll_RemoveAllSessions()
    {
        _store.SetSession("token1", TimeSpan.FromHours(1));
        _store.SetSession("token2", TimeSpan.FromHours(1));
        Assert.That(_store.Count, Is.EqualTo(2));

        _store.Clear();
        Assert.That(_store.Count, Is.EqualTo(0));
        Assert.That(_store.IsValid("token1"), Is.False);

        _store.SetSession("token3", TimeSpan.FromHours(1));
        Assert.That(_store.Count, Is.EqualTo(1));

        _store.InvalidateAll();
        Assert.That(_store.Count, Is.EqualTo(0));
        Assert.That(_store.IsValid("token3"), Is.False);
    }

    [Test]
    public void SharedSessionStore_ReturnsSingletonInstance()
    {
        var instance1 = RpcSessionStore.SharedSessionStore;
        var instance2 = RpcSessionStore.SharedSessionStore;

        Assert.That(instance1, Is.Not.Null);
        Assert.That(instance2, Is.SameAs(instance1));
    }

    [Test]
    public void ConcurrentOperations_AreThreadSafe()
    {
        var store = new RpcSessionStore(50);
        var tasks = new List<Task>();

        for (var t = 0; t < 8; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    var token = $"t{threadId}_{i}";
                    store.SetSession(token, TimeSpan.FromMinutes(10));
                    _ = store.IsValid(token);

                    if (i % 3 == 0)
                    {
                        store.RemoveSession(token);
                    }
                }
            }));
        }

        Assert.DoesNotThrowAsync(async () => await Task.WhenAll(tasks));
        Assert.That(store.Count, Is.LessThanOrEqualTo(50));
    }
}
