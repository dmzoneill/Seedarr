using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class SpaFallbackIntegrationTest : IntegrationTestBase
{
    [Test]
    public async Task Spa_deep_link_serves_index_html_when_authentication_enabled()
    {
        var configProvider = GlobalSetup.Factory.Services.GetRequiredService<IConfigFileProvider>();
        using var unauthenticatedClient = GlobalSetup.Factory.CreateClient();

        try
        {
            configProvider.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "AuthenticationEnabled", true }
            });

            var loginResponse = await unauthenticatedClient.GetAsync("/login");
            Assert.That(loginResponse.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(loginResponse.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));

            if (loginResponse.StatusCode == HttpStatusCode.OK)
            {
                var html = await loginResponse.Content.ReadAsStringAsync();
                Assert.That(html, Does.Contain("<html").IgnoreCase);
            }

            var dashboardResponse = await unauthenticatedClient.GetAsync("/dashboard");
            Assert.That(dashboardResponse.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(dashboardResponse.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
        }
        finally
        {
            configProvider.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "AuthenticationEnabled", false }
            });
        }
    }
}
