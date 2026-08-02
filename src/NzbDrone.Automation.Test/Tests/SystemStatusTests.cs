using NUnit.Framework;
using NzbDrone.Automation.Test.PageModels;

namespace NzbDrone.Automation.Test.Tests;

public class SystemStatusTests : AutomationTestBase
{
    private SystemPage _page;

    [SetUp]
    public void SetUpPage() => _page = new SystemPage(Driver, BaseUrl);

    [Test]
    public void SystemStatus_page_renders_sidebar()
    {
        _page.Navigate();
        Assert.That(_page.SidebarPresent, Is.True);
    }

    [Test]
    public void SystemStatus_page_renders_topbar()
    {
        _page.Navigate();
        Assert.That(_page.TopBarPresent, Is.True);
    }

    [Test]
    public void SystemStatus_page_has_no_severe_console_errors()
    {
        _page.Navigate();
        var errors = GetConsoleErrors();
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }
}
