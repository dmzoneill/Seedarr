using System.Threading.Tasks;
using NzbDrone.Core.Authentication;

namespace Seedarr.Http.Authentication;

public interface IDynamicAuthSchemeManager
{
    Task RegisterOrUpdateOidcProviderAsync(IdentityProviderDefinition provider);

    Task RemoveProviderSchemeAsync(string providerId);

    Task InitializeConfiguredProvidersAsync();
}
