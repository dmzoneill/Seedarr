using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Authentication;

public interface ILocalAdminCredentialService
{
    string HashPassword(string plainTextPassword);

    bool VerifyStoredPassword(string plainTextPassword, string storedPassword);

    string GetAdminUsername(IConfigService configService);

    bool IsApiKeyCredential(string username, string password, IConfigFileProvider configFileProvider);

    bool IsAdminPasswordCredential(string username, string password, IConfigService configService);

    bool ValidateLocalCredentials(string username, string password, IConfigFileProvider configFileProvider, IConfigService configService);
}
