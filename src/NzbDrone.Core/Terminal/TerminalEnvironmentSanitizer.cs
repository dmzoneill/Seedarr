#pragma warning disable SA1117
#pragma warning disable IDE0007

using System;
using System.Collections;
using System.Collections.Generic;

namespace NzbDrone.Core.Terminal;

public static class TerminalEnvironmentSanitizer
{
    public const string DefaultTerm = "xterm-256color";
    public const string DefaultLang = "en_US.UTF-8";
    public const string DefaultPath = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

    public static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "TERM",
        "COLORTERM",
        "LANG",
        "LC_ALL",
        "LC_CTYPE",
        "LC_MESSAGES",
        "LC_COLLATE",
        "PATH",
        "HOME",
        "USER",
        "LOGNAME",
        "SHELL",
        "TMPDIR",
        "TEMP",
        "TMP",
        "PWD",
        "SHLVL",
        "LINES",
        "COLUMNS",
        "SYSTEMROOT",
        "COMSPEC",
        "PATHEXT",
        "WINDIR",
        "APPDATA",
        "LOCALAPPDATA",
        "USERPROFILE",
        "HOMEPATH",
        "HOMEDRIVE"
    };

    private static readonly string[] SensitivePrefixes =
    {
        "SEEDARR_",
        "NZBDRONE_",
        "OIDC_",
        "DB_"
    };

    private static readonly string[] SensitiveKeywords =
    {
        "SECRET",
        "TOKEN",
        "API_KEY",
        "APIKEY",
        "PASSWORD",
        "PASSWD",
        "CREDENTIAL"
    };

    public static bool IsSensitive(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        foreach (var prefix in SensitivePrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var keyword in SensitiveKeywords)
        {
            if (key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsWhitelisted(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return Whitelist.Contains(key);
    }

    public static Dictionary<string, string> Sanitize(IDictionary<string, string> environment)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (environment != null)
        {
            foreach (var kvp in environment)
            {
                var key = kvp.Key;
                var val = kvp.Value;

                if (!string.IsNullOrWhiteSpace(key) && val != null && IsWhitelisted(key) && !IsSensitive(key))
                {
                    result[key] = val;
                }
            }
        }

        if (!result.ContainsKey("TERM") || string.IsNullOrWhiteSpace(result["TERM"]))
        {
            result["TERM"] = DefaultTerm;
        }

        if (!result.ContainsKey("LANG") || string.IsNullOrWhiteSpace(result["LANG"]))
        {
            result["LANG"] = DefaultLang;
        }

        return result;
    }

    public static Dictionary<string, string> Sanitize(Dictionary<string, string> environment)
    {
        return Sanitize((IDictionary<string, string>)environment);
    }

    public static Dictionary<string, string> Sanitize(IDictionary environment)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (environment != null)
        {
            foreach (DictionaryEntry entry in environment)
            {
                var key = entry.Key?.ToString();
                var val = entry.Value?.ToString();

                if (!string.IsNullOrWhiteSpace(key) && val != null)
                {
                    dict[key] = val;
                }
            }
        }

        return Sanitize((IDictionary<string, string>)dict);
    }

    public static Dictionary<string, string> GetSanitizedEnvironment(IDictionary<string, string> customOverrides = null)
    {
        var env = Sanitize(Environment.GetEnvironmentVariables());

        if (!env.ContainsKey("PATH") || string.IsNullOrWhiteSpace(env["PATH"]))
        {
            env["PATH"] = DefaultPath;
        }

        if (customOverrides != null)
        {
            var sanitizedOverrides = Sanitize(customOverrides);
            foreach (var kvp in sanitizedOverrides)
            {
                env[kvp.Key] = kvp.Value;
            }
        }

        return env;
    }
}
