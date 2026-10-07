using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.DownloadClients.Sync;
using Seedarr.Api.V1.DownloadClients;

namespace NzbDrone.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientSyncControllerTest
{
    private IDownloadClientSyncService _syncService;
    private DownloadClientSyncController _controller;

    [SetUp]
    public void SetUp()
    {
        _syncService = Substitute.For<IDownloadClientSyncService>();
        _controller = new DownloadClientSyncController(_syncService);
    }

    [Test]
    public void DownloadClientSyncController_should_have_proper_authorization_policies()
    {
        var classAttr = typeof(DownloadClientSyncController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(classAttr.Policy, Is.EqualTo(Policies.Reader));

        var syncAttr = typeof(DownloadClientSyncController).GetMethod(nameof(DownloadClientSyncController.Sync))!
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.That(syncAttr.Policy, Is.EqualTo(Policies.AdminOnly));
    }
}
