using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using Seedarr.Api.V1.TrackerBoost;

namespace NzbDrone.Core.Test.TrackerBoost;

[TestFixture]
public class TrackerBoostControllerTest
{
    [Test]
    public void Controller_should_have_Authorize_Reader_attribute()
    {
        var type = typeof(TrackerBoostController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.Reader));
    }

    [TestCase(nameof(TrackerBoostController.AddTracker))]
    [TestCase(nameof(TrackerBoostController.ScanTrackers))]
    [TestCase(nameof(TrackerBoostController.HarvestFromDownloads))]
    [TestCase(nameof(TrackerBoostController.HarvestProwlarr))]
    [TestCase(nameof(TrackerBoostController.HarvestFeeds))]
    [TestCase(nameof(TrackerBoostController.BoostTorrent))]
    [TestCase(nameof(TrackerBoostController.BoostHash))]
    [TestCase(nameof(TrackerBoostController.InjectTracker))]
    [TestCase(nameof(TrackerBoostController.BoostAllTorrents))]
    [TestCase(nameof(TrackerBoostController.RecoverMissingTrackers))]
    public void Operator_mutations_should_have_Authorize_Operator_attribute(string methodName)
    {
        var method = typeof(TrackerBoostController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == methodName);
        Assert.That(method, Is.Not.Null, $"Method {methodName} not found");

        var attr = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.Operator));
    }

    [TestCase(nameof(TrackerBoostController.UpdateSettings))]
    [TestCase(nameof(TrackerBoostController.DeleteTracker))]
    [TestCase(nameof(TrackerBoostController.ClearLogs))]
    public void Admin_mutations_should_have_Authorize_AdminOnly_attribute(string methodName)
    {
        var method = typeof(TrackerBoostController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == methodName);
        Assert.That(method, Is.Not.Null, $"Method {methodName} not found");

        var attr = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;
        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.AdminOnly));
    }
}
