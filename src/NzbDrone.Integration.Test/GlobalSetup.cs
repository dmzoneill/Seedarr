using NUnit.Framework;

[SetUpFixture]
public class GlobalSetup
{
    public static NzbDrone.Integration.Test.SeedarrWebApplicationFactory Factory { get; private set; }

    [OneTimeSetUp]
    public void SetUp()
    {
        Factory = new NzbDrone.Integration.Test.SeedarrWebApplicationFactory();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Factory?.Dispose();
    }
}
