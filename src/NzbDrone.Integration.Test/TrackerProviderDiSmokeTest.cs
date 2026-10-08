using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Http;
using NzbDrone.Core.Trackers.MultiTracker;
using NzbDrone.Core.Trackers.Udp;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TrackerProviderDiSmokeTest
{
    [Test]
    public void Resolves_http_and_udp_tracker_providers()
    {
        var services = GlobalSetup.Factory.Services;
        var serviceFactory = services.GetRequiredService<IServiceFactory>();
        var buildAll = serviceFactory.BuildAll<ITrackerProvider>().ToList();
        var getServices = services.GetServices<ITrackerProvider>().ToList();
        var http = services.GetService<HttpTrackerProvider>();
        var udp = services.GetService<UdpTrackerProvider>();
        var multi = services.GetRequiredService<IMultiTrackerManager>();

        TestContext.WriteLine($"BuildAll={buildAll.Count}, GetServices={getServices.Count}, http={http != null}, udp={udp != null}");

        Assert.That(buildAll, Is.Not.Empty);
        Assert.That(getServices, Is.Not.Empty);
        Assert.That(http, Is.Not.Null);
        Assert.That(udp, Is.Not.Null);
        Assert.That(multi, Is.Not.Null);
    }
}
