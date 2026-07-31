using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class SystemTests : IntegrationTestBase
{
    [Test]
    public async Task Status_returns_200()
    {
        var response = await GetAsync("/api/v1/system/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Status_has_appName_Seedarr()
    {
        var resource = await GetJsonAsync<Dictionary<string, object>>("/api/v1/system/status");

        Assert.That(resource.ContainsKey("appName"), Is.True);
        Assert.That(resource["appName"].ToString(), Is.EqualTo("Seedarr"));
    }

    [Test]
    public async Task Status_has_version_field()
    {
        var resource = await GetJsonAsync<Dictionary<string, object>>("/api/v1/system/status");

        Assert.That(resource.ContainsKey("version"), Is.True);
        Assert.That(resource["version"].ToString(), Is.Not.Empty);
    }

    [Test]
    public async Task Status_has_isLinux_field()
    {
        var resource = await GetJsonAsync<Dictionary<string, object>>("/api/v1/system/status");

        Assert.That(resource.ContainsKey("isLinux"), Is.True);
    }
}
