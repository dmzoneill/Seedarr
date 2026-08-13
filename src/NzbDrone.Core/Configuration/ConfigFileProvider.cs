using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Configuration;

public class ConfigFileProvider : IConfigFileProvider
{
    private readonly Dictionary<string, string> _config;

    public ConfigFileProvider()
    {
        _config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (ApiKey.Length == 0)
        {
            _config["ApiKey"] = GenerateApiKey();
        }
    }

    public string BindAddress => GetValue("BindAddress", "*");
    public int Port => GetValueInt("Port", 9898);
    public bool EnableSsl => GetValueBool("EnableSsl", false);
    public string ApiKey => GetValue("ApiKey", string.Empty);
    public bool AuthenticationEnabled => GetValueBool("AuthenticationEnabled", false);
    public string LogLevel => GetValue("LogLevel", "info");
    public string UrlBase => GetValue("UrlBase", string.Empty);
    public string PostgresHost => GetValue("PostgresHost", string.Empty);
    public int PostgresPort => GetValueInt("PostgresPort", 5432);
    public string PostgresMainDb => GetValue("PostgresMainDb", string.Empty);
    public string PostgresUser => GetValue("PostgresUser", string.Empty);
    public string PostgresPassword => GetValue("PostgresPassword", string.Empty);

    private string GetValue(string key, string defaultValue)
    {
        return _config.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private int GetValueInt(string key, int defaultValue)
    {
        var value = GetValue(key, null);
        return value != null && int.TryParse(value, out var result) ? result : defaultValue;
    }

    private bool GetValueBool(string key, bool defaultValue)
    {
        var value = GetValue(key, null);
        return value != null && bool.TryParse(value, out var result) ? result : defaultValue;
    }

    private static string GenerateApiKey()
    {
        return Guid.NewGuid().ToString("N");
    }
}
