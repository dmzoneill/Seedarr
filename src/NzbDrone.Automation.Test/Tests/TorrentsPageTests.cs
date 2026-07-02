using NUnit.Framework;
using NzbDrone.Automation.Test.PageModels;

namespace NzbDrone.Automation.Test.Tests;

public class TorrentsPageTests : AutomationTestBase
{
    private TorrentsPage _page;

    [SetUp]
    public void SetUpPage() => _page = new TorrentsPage(Driver, BaseUrl);

    [Test]
    public void Torrents_page_renders_sidebar()
    {
        _page.Navigate();
        Assert.That(_page.SidebarPresent, Is.True);
    }

    [Test]
    public void Torrents_page_has_search_input()
    {
        _page.Navigate();
        Assert.That(_page.SearchInputPresent, Is.True);
    }

    [Test]
    public void Torrents_page_has_no_severe_console_errors()
    {
        _page.Navigate();
        var errors = GetConsoleErrors();
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
    }
}
