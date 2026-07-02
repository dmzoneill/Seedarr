using System.Collections.Generic;
using OpenQA.Selenium;

namespace NzbDrone.Automation.Test.PageModels;

public abstract class BasePage
{
    protected readonly IWebDriver Driver;
    protected readonly string BaseUrl;

    protected BasePage(IWebDriver driver, string baseUrl)
    {
        Driver = driver;
        BaseUrl = baseUrl;
    }

    public abstract string Path { get; }

    public void Navigate() => Driver.Navigate().GoToUrl($"{BaseUrl}{Path}");

    public bool SidebarPresent => Driver.FindElements(By.CssSelector(".sidebar")).Count > 0;

    public IList<IWebElement> NavItems => Driver.FindElements(By.CssSelector(".sidebar-nav-item"));

    public bool TopBarPresent => Driver.FindElements(By.CssSelector(".topbar")).Count > 0;

    public string PageTitle => Driver.Title;
}
