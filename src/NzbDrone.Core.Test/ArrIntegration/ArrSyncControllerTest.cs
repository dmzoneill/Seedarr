using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.ArrIntegration;
using Seedarr.Api.V1.ArrIntegration;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class ArrSyncControllerTest
{
    private IArrSyncService _arrSyncService;
    private ArrSyncController _controller;

    [SetUp]
    public void SetUp()
    {
        _arrSyncService = Substitute.For<IArrSyncService>();
        _controller = new ArrSyncController(_arrSyncService);
    }

    [Test]
    public void ArrSyncController_should_have_proper_authorization_policies()
    {
        var classAttr = typeof(ArrSyncController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(classAttr.Policy, Is.EqualTo(Policies.Reader));

        var syncAttr = typeof(ArrSyncController).GetMethod(nameof(ArrSyncController.Sync))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(syncAttr.Policy, Is.EqualTo(Policies.AdminOnly));
    }
}
