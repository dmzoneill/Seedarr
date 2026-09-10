using NUnit.Framework;
using OpenQA.Selenium;

namespace NzbDrone.Automation.Test.Tests;

public class NavigationTests : AutomationTestBase
{
    [Test]
    public void Clicking_Torrents_nav_navigates_to_torrents()
    {
        NavigateTo("/");
        var torrentsLink = Driver.FindElement(By.CssSelector("a[href='/torrents']"));
        torrentsLink.Click();
        Assert.That(Driver.Url, Does.Contain("/torrents"));
    }

    [Test]
    public void Torrents_nav_expands_subitems()
    {
        NavigateTo("/torrents");
        var subItems = Driver.FindElements(By.CssSelector(".sidebar-nav-sub"));
        Assert.That(subItems.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void Clicking_Activity_nav_navigates_to_activity_history()
    {
        NavigateTo("/");
        var activityLink = Driver.FindElement(By.CssSelector("a[href='/activity/history']"));
        activityLink.Click();
        Assert.That(Driver.Url, Does.Contain("/activity/history"));
    }

    [Test]
    public void Activity_nav_expands_subitems()
    {
        NavigateTo("/activity/history");
        var subItems = Driver.FindElements(By.CssSelector(".sidebar-nav-sub"));
        Assert.That(subItems.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void Legacy_activity_torrents_redirects_to_torrents()
    {
        NavigateTo("/activity/torrents");
        Assert.That(Driver.Url, Does.Contain("/torrents"));
    }

    [Test]
    public void Legacy_torrents_history_redirects_to_activity_history()
    {
        NavigateTo("/torrents/history");
        Assert.That(Driver.Url, Does.Contain("/activity/history"));
    }

    [Test]
    public void Clicking_Settings_nav_navigates_to_settings()
    {
        NavigateTo("/");
        var settingsLink = Driver.FindElement(By.CssSelector("a[href='/settings/general']"));
        settingsLink.Click();
        Assert.That(Driver.Url, Does.Contain("/settings"));
    }

    [Test]
    public void Settings_nav_expands_subitems()
    {
        NavigateTo("/settings/general");
        var subItems = Driver.FindElements(By.CssSelector(".sidebar-nav-sub"));
        Assert.That(subItems.Count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void Clicking_System_nav_navigates_to_system_status()
    {
        NavigateTo("/");
        var systemLink = Driver.FindElement(By.CssSelector("a[href='/system/status']"));
        systemLink.Click();
        Assert.That(Driver.Url, Does.Contain("/system"));
    }

    [Test]
    public void System_nav_expands_subitems()
    {
        NavigateTo("/system/status");
        var subItems = Driver.FindElements(By.CssSelector(".sidebar-nav-sub"));
        Assert.That(subItems.Count, Is.GreaterThanOrEqualTo(4));
    }
}
