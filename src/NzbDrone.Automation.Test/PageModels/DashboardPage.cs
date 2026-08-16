using OpenQA.Selenium;

namespace NzbDrone.Automation.Test.PageModels;

public class DashboardPage : BasePage
{
    public DashboardPage(IWebDriver driver, string baseUrl)
        : base(driver, baseUrl)
    {
    }

    public override string Path => "/";

    public bool StatsCardPresent => Driver.FindElements(By.CssSelector(".stats-card, .dashboard-stat, [class*='stat']")).Count > 0;
}
