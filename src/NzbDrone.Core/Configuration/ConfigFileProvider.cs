// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Configuration;

public class ConfigFileProvider : IConfigFileProvider
{
    private const string ConfigFileName = "config.xml";
    private const string ConfigElementName = "Config";

    private readonly string _configFile;
    private readonly Dictionary<string, string> _config;
    private static readonly object Mutex = new();

    public ConfigFileProvider(IAppFolderInfo appFolderInfo)
    {
        if (appFolderInfo == null)
        {
            throw new ArgumentNullException(nameof(appFolderInfo));
        }

        _configFile = Path.Combine(appFolderInfo.AppDataFolder, ConfigFileName);
        _config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        LoadFromFile();

        if (string.IsNullOrEmpty(ApiKey))
        {
            SetValue("ApiKey", GenerateApiKey());
        }
    }

    public string BindAddress => GetValue("BindAddress", "*");

    public int Port => GetValueInt("Port", 9898);

    public bool EnableSsl => GetValueBool("EnableSsl", false);

    public int SslPort => GetValueInt("SslPort", 9899);

    public string SslCertPath => GetValue("SslCertPath", string.Empty);

    public string SslKeyPath => GetValue("SslKeyPath", string.Empty);

    public string SslCertPassword => GetValue("SslCertPassword", string.Empty);

    public bool RedirectHttpToHttps => GetValueBool("RedirectHttpToHttps", false);

    public string ApiKey => GetValue("ApiKey", string.Empty);

    public bool AuthenticationEnabled => GetValueBool("AuthenticationEnabled", false);

    public bool TerminalAccessEnabled => GetValueBool("TerminalAccessEnabled", true);

    public string LogLevel => GetValue("LogLevel", "info");

    public string UrlBase => GetValue("UrlBase", string.Empty);

    public string PostgresHost => GetValue("PostgresHost", string.Empty);

    public int PostgresPort => GetValueInt("PostgresPort", 5432);

    public string PostgresMainDb => GetValue("PostgresMainDb", string.Empty);

    public string PostgresUser => GetValue("PostgresUser", string.Empty);

    public string PostgresPassword => GetValue("PostgresPassword", string.Empty);

    public string BindInterface => GetValue("BindInterface", string.Empty);

    public bool EnableVpnKillSwitch => GetValueBool("EnableVpnKillSwitch", false);

    public bool ForceProxy => GetValueBool("ForceProxy", false);

    public bool AnonymousMode => GetValueBool("AnonymousMode", false);

    public bool EnableIPv6 => GetValueBool("EnableIPv6", true);

    public int MaxConnectionsPerIp => GetValueInt("MaxConnectionsPerIp", 5);

    public int MaximumHalfOpenConnections => GetValueInt("MaximumHalfOpenConnections", 50);

    public int PeerDscp => GetValueInt("PeerDscp", 0);

    public int PeerTos => GetValueInt("PeerTos", 0);

    private void LoadFromFile()
    {
        lock (Mutex)
        {
            if (!File.Exists(_configFile))
            {
                return;
            }

            var xDoc = XDocument.Load(_configFile);
            var config = xDoc.Element(ConfigElementName);
            if (config == null)
            {
                return;
            }

            foreach (var element in config.Elements())
            {
                _config[element.Name.LocalName] = element.Value.Trim();
            }
        }
    }

    private void SetValue(string key, string value)
    {
        lock (Mutex)
        {
            _config[key] = value;
            SaveToFile();
        }
    }

    private void SaveToFile()
    {
        lock (Mutex)
        {
            var configElement = new XElement(ConfigElementName);
            foreach (var kvp in _config)
            {
                configElement.Add(new XElement(kvp.Key, kvp.Value));
            }

            var xDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), configElement);
            xDoc.Save(_configFile);
        }
    }

    private string GetValue(string key, string defaultValue)
    {
        var snakeKey = ToSnakeCaseUpper(key);
        var upperKey = key.ToUpperInvariant();

        var envVal = Environment.GetEnvironmentVariable("SEEDARR__" + snakeKey)
            ?? Environment.GetEnvironmentVariable("SEEDARR_" + snakeKey)
            ?? Environment.GetEnvironmentVariable("SEEDARR__" + upperKey)
            ?? Environment.GetEnvironmentVariable("SEEDARR_" + upperKey)
            ?? Environment.GetEnvironmentVariable("SEEDARR__" + key)
            ?? Environment.GetEnvironmentVariable("SEEDARR_" + key);

        if (!string.IsNullOrWhiteSpace(envVal))
        {
            return envVal;
        }

        lock (Mutex)
        {
            return _config.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }

    private static string ToSnakeCaseUpper(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = System.Text.RegularExpressions.Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"([A-Z]+)([A-Z][a-z])", "$1_$2");
        return result.ToUpperInvariant();
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

    public void SaveConfigDictionary(Dictionary<string, object> configValues)
    {
        if (configValues == null)
        {
            return;
        }

        lock (Mutex)
        {
            foreach (var kvp in configValues)
            {
                if (kvp.Value != null)
                {
                    _config[kvp.Key] = kvp.Value.ToString();
                }
            }

            SaveToFile();
        }
    }

    private static string GenerateApiKey()
    {
        return Guid.NewGuid().ToString("N");
    }
}
