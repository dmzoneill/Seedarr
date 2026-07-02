using OpenQA.Selenium;

namespace NzbDrone.Automation.Test.PageModels;

public class SystemPage : BasePage
{
    public SystemPage(IWebDriver driver, string baseUrl)
        : base(driver, baseUrl)
    {
    }

    public override string Path => "/system/status";

    public bool StatusInfoPresent =>
        Driver.FindElements(By.CssSelector("[class*='status'], [class*='system'], .app-name")).Count > 0;
}
