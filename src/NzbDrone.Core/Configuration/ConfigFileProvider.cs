// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Configuration;

public class ConfigFileProvider : IConfigFileProvider
{
    private const string ConfigFileName = "config.xml";
    private const string ConfigElementName = "Config";

    private static readonly object Mutex = new();

    private readonly Logger _logger;
    private readonly string _configFile;
    private readonly Dictionary<string, string> _config;

    public ConfigFileProvider(IAppFolderInfo appFolderInfo)
    {
        if (appFolderInfo == null)
        {
            throw new ArgumentNullException(nameof(appFolderInfo));
        }

        _logger = LogManager.GetCurrentClassLogger();
        _configFile = Path.Combine(appFolderInfo.AppDataFolder, ConfigFileName);
        _config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        LoadFromFile();

        lock (Mutex)
        {
            var needsSave = false;
            if (!_config.ContainsKey("Port"))
            {
                _config["Port"] = "9898";
                needsSave = true;
            }

            if (!_config.ContainsKey("UrlBase"))
            {
                _config["UrlBase"] = string.Empty;
            }

            if (!_config.ContainsKey("BindAddress"))
            {
                _config["BindAddress"] = "*";
            }

            if (string.IsNullOrEmpty(ApiKey))
            {
                _config["ApiKey"] = GenerateApiKey();
                needsSave = true;
            }

            if (needsSave)
            {
                SaveToFile();
            }
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

    public bool IsSetupCompleted => GetValueBool("IsSetupCompleted", false);

    public void SetIsSetupCompleted(bool isCompleted)
    {
        SetValue("IsSetupCompleted", isCompleted ? "true" : "false");
    }

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

    public int VpnStabilizationDelaySeconds => GetValueInt("VpnStabilizationDelaySeconds", 8);

    public bool ForceProxy => GetValueBool("ForceProxy", false);

    public bool AnonymousMode => GetValueBool("AnonymousMode", false);

    public bool EnableIPv6 => GetValueBool("EnableIPv6", true);

    public int MaxConnectionsPerIp => GetValueInt("MaxConnectionsPerIp", 5);

    public int MaximumHalfOpenConnections => GetValueInt("MaximumHalfOpenConnections", 50);

    public int PeerDscp => GetValueInt("PeerDscp", 0);

    public int PeerTos => GetValueInt("PeerTos", 0);

    public string TrustedProxies => GetValue("TrustedProxies", string.Empty);

    public string AllowedOrigins => GetValue("AllowedOrigins", string.Empty);

    public List<string> AllowedOriginsList
    {
        get
        {
            var allowedOrigins = AllowedOrigins;
            if (string.IsNullOrWhiteSpace(allowedOrigins))
            {
                return new List<string>();
            }

            return allowedOrigins
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim())
                .Where(o => !string.IsNullOrEmpty(o))
                .ToList();
        }
    }

    private void LoadFromFile()
    {
        lock (Mutex)
        {
            var backupFile = _configFile + ".bak";

            if (File.Exists(_configFile))
            {
                if (TryLoadConfig(_configFile, out var loadedConfig))
                {
                    _config.Clear();
                    foreach (var kvp in loadedConfig)
                    {
                        _config[kvp.Key] = kvp.Value;
                    }

                    return;
                }

                _logger.Warn("Failed to load config file at {0}. File is empty, corrupted, or missing root element. Attempting recovery from backup...", _configFile);
            }

            if (File.Exists(backupFile))
            {
                if (TryLoadConfig(backupFile, out var backupConfig))
                {
                    _logger.Info("Successfully recovered configuration from backup at {0}", backupFile);
                    _config.Clear();
                    foreach (var kvp in backupConfig)
                    {
                        _config[kvp.Key] = kvp.Value;
                    }

                    try
                    {
                        var tempFile = _configFile + ".tmp";
                        File.Copy(backupFile, tempFile, overwrite: true);
                        File.Move(tempFile, _configFile, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to restore primary config file from backup {0}", backupFile);
                    }

                    return;
                }

                _logger.Warn("Backup config file at {0} is also corrupted or invalid.", backupFile);
            }

            if (File.Exists(_configFile) || File.Exists(backupFile))
            {
                _logger.Warn("Both primary config file and backup at {0} are invalid. Initializing new default configuration.", _configFile);
            }

            _config.Clear();
        }
    }

    private bool TryLoadConfig(string path, out Dictionary<string, string> configValues)
    {
        configValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length == 0)
            {
                return false;
            }

            var xDoc = XDocument.Load(path);
            var root = xDoc.Element(ConfigElementName);
            if (root == null)
            {
                return false;
            }

            foreach (var element in root.Elements())
            {
                configValues[element.Name.LocalName] = element.Value.Trim();
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Unexpected error loading config file at {0}", path);
            return false;
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
            var directory = Path.GetDirectoryName(_configFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _configFile + ".tmp";
            var backupPath = _configFile + ".bak";

            var configElement = new XElement(ConfigElementName);
            foreach (var kvp in _config)
            {
                configElement.Add(new XElement(kvp.Key, kvp.Value));
            }

            var xDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), configElement);

            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                xDoc.Save(fileStream);
                fileStream.Flush(true);
            }

            if (File.Exists(_configFile) && TryLoadConfig(_configFile, out _))
            {
                try
                {
                    File.Copy(_configFile, backupPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to create backup config file at {0}", backupPath);
                }
            }

            try
            {
                File.Move(tempPath, _configFile, overwrite: true);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Ignore cleanup error
                    }
                }

                throw;
            }
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
