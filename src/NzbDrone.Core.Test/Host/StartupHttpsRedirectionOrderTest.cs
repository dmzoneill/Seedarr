using System.IO;
using NUnit.Framework;

namespace NzbDrone.Core.Test.Host;

[TestFixture]
public class StartupHttpsRedirectionOrderTest
{
    [Test]
    public void Configure_registers_https_redirection_before_static_files()
    {
        var source = File.ReadAllText(LocateStartupSource());
        var configureStart = source.IndexOf("public void Configure(WebApplication app)", System.StringComparison.Ordinal);
        Assert.That(configureStart, Is.GreaterThanOrEqualTo(0), "Configure method must exist");

        var configureBody = source[configureStart..];
        var httpsIndex = configureBody.IndexOf("app.UseHttpsRedirection()", System.StringComparison.Ordinal);
        var staticIndex = configureBody.IndexOf("app.UseStaticFiles", System.StringComparison.Ordinal);

        Assert.That(httpsIndex, Is.GreaterThanOrEqualTo(0), "UseHttpsRedirection must be registered in Configure");
        Assert.That(staticIndex, Is.GreaterThanOrEqualTo(0), "UseStaticFiles must be registered in Configure");
        Assert.That(httpsIndex, Is.LessThan(staticIndex), "HTTPS redirection must run before static files");
    }

    private static string LocateStartupSource()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        for (var depth = 0; depth < 10; depth++)
        {
            var fromSrc = Path.Combine(dir, "src", "NzbDrone.Host", "Startup.cs");
            if (File.Exists(fromSrc))
            {
                return fromSrc;
            }

            var sibling = Path.Combine(dir, "NzbDrone.Host", "Startup.cs");
            if (File.Exists(sibling))
            {
                return sibling;
            }

            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            dir = parent;
        }

        throw new FileNotFoundException("Could not locate Startup.cs for pipeline order assertion");
    }
}
