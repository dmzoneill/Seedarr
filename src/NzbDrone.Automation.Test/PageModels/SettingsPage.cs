using System.Collections.Generic;
using OpenQA.Selenium;

namespace NzbDrone.Automation.Test.PageModels;

public class SettingsPage : BasePage
{
    public SettingsPage(IWebDriver driver, string baseUrl)
        : base(driver, baseUrl)
    {
    }

    public override string Path => "/settings/general";

    public IList<IWebElement> SubNavItems =>
        Driver.FindElements(By.CssSelector(".sidebar-nav-sub"));

    public bool FormPresent =>
        Driver.FindElements(By.CssSelector("form, .settings-form, [class*='settings']")).Count > 0;
}
