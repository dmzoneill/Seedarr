using System.Threading;
using NUnit.Framework;
using OpenQA.Selenium;

namespace NzbDrone.Automation.Test.Tests;

public class GettingStartedGuideTests : AutomationTestBase
{
    [Test]
    public void Can_open_and_navigate_getting_started_guide()
    {
        NavigateTo("/");

        // Open Actions dropdown
        var actionsBtn = Driver.FindElement(By.CssSelector(".topbar-actions button[title='Actions']"));
        actionsBtn.Click();

        Thread.Sleep(300);

        // Click "Getting Started Guide"
        var guideItem = Driver.FindElement(By.XPath("//button[contains(text(), 'Getting Started Guide')]"));
        guideItem.Click();

        Thread.Sleep(500);

        // Verify Getting Started modal is displayed
        Assert.That(ElementExists(By.CssSelector(".modal")), Is.True);
        Assert.That(Driver.FindElement(By.CssSelector(".modal-title")).Text, Does.Contain("Welcome to Seedarr"));

        // Click "Start Example Tour" to go to Step 1 (Download Client)
        var startTourBtn = Driver.FindElement(By.XPath("//button[contains(text(), 'Start Example Tour')]"));
        startTourBtn.Click();

        Thread.Sleep(300);

        // Verify Step 1 Download Client is displayed
        Assert.That(Driver.FindElement(By.CssSelector(".modal-title")).Text, Does.Contain("Add Download Client"));

        // Click "Next" to go to Step 2 (Prowlarr)
        var nextBtn = Driver.FindElement(By.XPath("//button[contains(text(), 'Next')]"));
        nextBtn.Click();

        Thread.Sleep(300);

        // Verify Step 2 Prowlarr is displayed
        Assert.That(Driver.FindElement(By.CssSelector(".modal-title")).Text, Does.Contain("Add Indexer"));

        // Close modal
        var closeBtn = Driver.FindElement(By.CssSelector(".modal button[title*='Close']"));
        closeBtn.Click();

        Thread.Sleep(300);

        // Verify modal is closed
        Assert.That(ElementExists(By.CssSelector(".modal")), Is.False);
    }
}
