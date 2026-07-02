using OpenQA.Selenium;

namespace NzbDrone.Automation.Test.PageModels;

public class TorrentsPage : BasePage
{
    public TorrentsPage(IWebDriver driver, string baseUrl)
        : base(driver, baseUrl)
    {
    }

    public override string Path => "/torrents";

    public bool TableOrGridPresent =>
        Driver.FindElements(By.CssSelector(".torrent-table, .torrent-grid, table, [class*='torrent']")).Count > 0;

    public bool SearchInputPresent =>
        Driver.FindElements(By.CssSelector(".topbar-search-input, input[placeholder='Search']")).Count > 0;
}
