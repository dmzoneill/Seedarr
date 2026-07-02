using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace NzbDrone.Automation.Test;

[TestFixture]
[Category("AutomationTest")]
public abstract class AutomationTestBase
{
    protected IWebDriver Driver { get; private set; }
    protected string BaseUrl => GlobalSetup.BaseUrl;

    [SetUp]
    public void SetUpDriver()
    {
        var options = new ChromeOptions();
        options.AddArguments(
            "--headless=new",
            "--no-sandbox",
            "--disable-dev-shm-usage",
            "--disable-gpu",
            "--window-size=1280,900");
        options.SetLoggingPreference(LogType.Browser, LogLevel.All);

        Driver = new ChromeDriver(options);
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
    }

    [TearDown]
    public void TearDownDriver()
    {
        Driver?.Quit();
        Driver?.Dispose();
        Driver = null;
    }

    protected IList<string> GetConsoleErrors()
    {
        try
        {
            var logs = Driver.Manage().Logs.GetLog(LogType.Browser);
            return logs
                .Where(l => l.Level == LogLevel.Severe)
                .Select(l => l.Message)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    protected WebDriverWait Wait(int seconds = 10) =>
        new(Driver, TimeSpan.FromSeconds(seconds));

    protected bool ElementExists(By by) =>
        Driver.FindElements(by).Count > 0;

    protected void NavigateTo(string path) =>
        Driver.Navigate().GoToUrl($"{BaseUrl}{path}");
}
