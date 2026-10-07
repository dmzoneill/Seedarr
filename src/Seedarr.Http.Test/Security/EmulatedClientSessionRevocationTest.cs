using System;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Seedarr.Http.Security;

namespace Seedarr.Http.Test.Security;

[TestFixture]
public class EmulatedClientSessionRevocationTest
{
    [Test]
    public void RevokeAll_ClearsRpcSessions()
    {
        var store = new RpcSessionStore();
        store.SetSession("sid-token", TimeSpan.FromDays(7));
        Assert.That(store.IsValid("sid-token"), Is.True);

        EmulatedClientSessionRevocation.RevokeAll(store, new DefaultHttpContext().Response);

        Assert.That(store.IsValid("sid-token"), Is.False);
        Assert.That(store.Count, Is.Zero);
    }
}
