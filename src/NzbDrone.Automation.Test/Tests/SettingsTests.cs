using NUnit.Framework;
using NzbDrone.Automation.Test.PageModels;

namespace NzbDrone.Automation.Test.Tests;

public class SettingsTests : AutomationTestBase
{
    private SettingsPage _page;

    [SetUp]
    public void SetUpPage() => _page = new SettingsPage(Driver, BaseUrl);

    [Test]
    public void Settings_page_renders_sidebar()
    {
        _page.Navigate();
        Assert.That(_page.SidebarPresent, Is.True);
    }

    [Test]
    public void Settings_page_shows_sub_nav_items()
    {
        _page.Navigate();
        Assert.That(_page.SubNavItems.Count, Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void Settings_page_has_no_severe_console_errors()
    {
        _page.Navigate();
        var errors = GetConsoleErrors();
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }
}
