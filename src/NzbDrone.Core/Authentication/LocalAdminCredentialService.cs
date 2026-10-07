using System;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Authentication;

public class LocalAdminCredentialService : ILocalAdminCredentialService
{
    public string HashPassword(string plainTextPassword)
    {
        return AdminPasswordHasher.HashPassword(plainTextPassword);
    }

    public bool VerifyStoredPassword(string plainTextPassword, string storedPassword)
    {
        return AdminPasswordHasher.VerifyPassword(plainTextPassword, storedPassword);
    }

    public string GetAdminUsername(IConfigService configService)
    {
        var configured = configService?.GetValue("AdminUsername", string.Empty);
        return string.IsNullOrWhiteSpace(configured) ? "admin" : configured.Trim();
    }

    public bool IsApiKeyCredential(string username, string password, IConfigFileProvider configFileProvider)
    {
        var apiKey = configFileProvider?.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        return AdminPasswordHasher.FixedTimeEquals(password, apiKey)
            || AdminPasswordHasher.FixedTimeEquals(username, apiKey);
    }

    public bool IsAdminPasswordCredential(string username, string password, IConfigService configService)
    {
        if (configService == null || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var storedPassword = configService.GetValue("AdminPassword", string.Empty);
        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            return false;
        }

        var adminUsername = GetAdminUsername(configService);
        var enteredUsername = string.IsNullOrWhiteSpace(username) ? adminUsername : username.Trim();
        if (!string.Equals(enteredUsername, adminUsername, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return VerifyStoredPassword(password, storedPassword);
    }

    public bool ValidateLocalCredentials(
        string username,
        string password,
        IConfigFileProvider configFileProvider,
        IConfigService configService)
    {
        return IsApiKeyCredential(username, password, configFileProvider)
            || IsAdminPasswordCredential(username, password, configService);
    }
}
