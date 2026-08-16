using NUnit.Framework;
using NzbDrone.Automation.Test.PageModels;

namespace NzbDrone.Automation.Test.Tests;

public class DashboardTests : AutomationTestBase
{
    private DashboardPage _page;

    [SetUp]
    public void SetUpPage() => _page = new DashboardPage(Driver, BaseUrl);

    [Test]
    public void Dashboard_page_title_contains_Seedarr()
    {
        _page.Navigate();
        Assert.That(_page.PageTitle, Does.Contain("Seedarr").IgnoreCase);
    }

    [Test]
    public void Dashboard_renders_sidebar()
    {
        _page.Navigate();
        Assert.That(_page.SidebarPresent, Is.True);
    }

    [Test]
    public void Dashboard_renders_topbar()
    {
        _page.Navigate();
        Assert.That(_page.TopBarPresent, Is.True);
    }

    [Test]
    public void Dashboard_has_at_least_four_nav_items()
    {
        _page.Navigate();
        Assert.That(_page.NavItems.Count, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void Dashboard_has_no_severe_console_errors()
    {
        _page.Navigate();
        var errors = GetConsoleErrors();
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }
}
