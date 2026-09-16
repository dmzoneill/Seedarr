using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Seedarr.Api.V1.Network;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class NetworkControllerIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task GetInterfaces_returns_200_with_list_of_network_interfaces()
    {
        var response = await GetAsync("/api/v1/network/interfaces");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var interfaces = await GetJsonAsync<List<NetworkInterfaceResource>>("/api/v1/network/interfaces");
        Assert.That(interfaces, Is.Not.Null);

        foreach (var iface in interfaces)
        {
            Assert.That(string.IsNullOrWhiteSpace(iface.Name), Is.False);
            Assert.That(iface.Type, Is.Not.Null);
            Assert.That(iface.Status, Is.Not.Null);
            Assert.That(iface.Addresses, Is.Not.Null);
        }
    }
}
