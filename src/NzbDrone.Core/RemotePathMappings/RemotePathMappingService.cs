using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace NzbDrone.Core.RemotePathMappings;

public class RemotePathMappingService : IRemotePathMappingService
{
    private readonly List<RemotePathMapping> _mappings = new();
    private readonly object _lock = new();
    private readonly ICallerHostResolver _callerHostResolver;
    private int _nextId = 1;

    public RemotePathMappingService(
        IEnumerable<RemotePathMapping> initialMappings = null,
        ICallerHostResolver callerHostResolver = null)
    {
        _callerHostResolver = callerHostResolver ?? new CallerHostResolver();
        if (initialMappings != null)
        {
            foreach (var m in initialMappings)
            {
                Add(m);
            }
        }
    }

    public List<RemotePathMapping> All()
    {
        lock (_lock)
        {
            return _mappings.Select(Clone).ToList();
        }
    }

    public RemotePathMapping Get(int id)
    {
        lock (_lock)
        {
            var mapping = _mappings.FirstOrDefault(m => m.Id == id);
            return mapping != null ? Clone(mapping) : null;
        }
    }

    public RemotePathMapping Add(RemotePathMapping mapping)
    {
        if (mapping == null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        lock (_lock)
        {
            var copy = Clone(mapping);
            copy.Id = _nextId++;
            _mappings.Add(copy);
            return Clone(copy);
        }
    }

    public void Update(RemotePathMapping mapping)
    {
        if (mapping == null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        lock (_lock)
        {
            var index = _mappings.FindIndex(m => m.Id == mapping.Id);
            if (index >= 0)
            {
                _mappings[index] = Clone(mapping);
            }
        }
    }

    public void Delete(int id)
    {
        lock (_lock)
        {
            _mappings.RemoveAll(m => m.Id == id);
        }
    }

    public string Remap(string host, string remotePath)
    {
        return RemapRemoteToLocal(host, remotePath);
    }

    public string RemapRemoteToLocal(string host, string remotePath)
    {
        if (string.IsNullOrEmpty(remotePath) || string.IsNullOrWhiteSpace(host))
        {
            return remotePath;
        }

        return TestMapping(host, remotePath, "remoteToLocal").MappedPath;
    }

    public string RemapLocalToRemote(string host, string localPath)
    {
        if (string.IsNullOrEmpty(localPath) || string.IsNullOrWhiteSpace(host))
        {
            return localPath;
        }

        return TestMapping(host, localPath, "localToRemote").MappedPath;
    }

    private static RemotePathMapping Clone(RemotePathMapping source)
    {
        return new RemotePathMapping
        {
            Id = source.Id,
            Host = source.Host,
            RemotePath = source.RemotePath,
            LocalPath = source.LocalPath,
        };
    }

    public RemotePathMappingTestResult TestMapping(string host, string path, string direction = "remoteToLocal")
    {
        var result = new RemotePathMappingTestResult
        {
            InputPath = path,
            MappedPath = path,
            RuleApplied = false,
            LocalPathExists = CheckPathExists(path),
        };

        if (string.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(host))
        {
            return result;
        }

        var isLocalToRemote = string.Equals(direction, "localToRemote", StringComparison.OrdinalIgnoreCase);
        var comparison = GetPathComparison(path);

        var candidates = GetMatchingCandidates(host, isLocalToRemote);
        if (candidates.Count == 0)
        {
            return result;
        }

        foreach (var mapping in candidates)
        {
            var sourcePrefix = isLocalToRemote ? mapping.LocalPath : mapping.RemotePath;
            var targetPrefix = isLocalToRemote ? mapping.RemotePath : mapping.LocalPath;

            if (IsPathPrefixMatch(path, sourcePrefix, comparison))
            {
                var mappedPath = TranslatePath(path, sourcePrefix, targetPrefix);

                result.MappedPath = mappedPath;
                result.RuleApplied = true;
                result.MatchedRuleId = mapping.Id;
                result.MatchedRuleHost = mapping.Host;
                result.MatchedRemotePrefix = mapping.RemotePath;
                result.MatchedLocalPrefix = mapping.LocalPath;
                result.LocalPathExists = isLocalToRemote ? CheckPathExists(path) : CheckPathExists(mappedPath);
                return result;
            }
        }

        return result;
    }

    public static string ExtractIpOrHostname(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = input.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(8);
        }

        var slashIdx = trimmed.IndexOfAny(new[] { '/', '\\' });
        if (slashIdx >= 0)
        {
            if (!(int.TryParse(trimmed.AsSpan(slashIdx + 1), out _) && IPAddress.TryParse(trimmed.AsSpan(0, slashIdx), out _)))
            {
                trimmed = trimmed.Substring(0, slashIdx);
            }
        }

        var atIdx = trimmed.IndexOf('@');
        if (atIdx >= 0)
        {
            trimmed = trimmed.Substring(atIdx + 1);
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.Contains(']'))
        {
            var closingBracket = trimmed.IndexOf(']');
            return trimmed.Substring(1, closingBracket - 1);
        }

        if (trimmed.Count(c => c == ':') == 1)
        {
            var colonIdx = trimmed.IndexOf(':');
            trimmed = trimmed.Substring(0, colonIdx);
        }

        return trimmed.TrimEnd('.');
    }

    public static bool IsCidrNotation(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var parts = host.Trim().Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0].Trim(), out var ip))
        {
            return false;
        }

        if (!int.TryParse(parts[1].Trim(), out var mask))
        {
            return false;
        }

        var maxBits = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return mask >= 0 && mask <= maxBits;
    }

    public static bool IsIpInCidr(string clientIpStr, string cidrStr)
    {
        if (string.IsNullOrWhiteSpace(clientIpStr) || string.IsNullOrWhiteSpace(cidrStr))
        {
            return false;
        }

        var cleanClientIp = ExtractIpOrHostname(clientIpStr);
        if (!IPAddress.TryParse(cleanClientIp, out var clientIp))
        {
            return false;
        }

        var parts = cidrStr.Trim().Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0].Trim(), out var networkIp) || !int.TryParse(parts[1].Trim(), out var prefixLength))
        {
            return false;
        }

        if (clientIp.IsIPv4MappedToIPv6)
        {
            clientIp = clientIp.MapToIPv4();
        }

        if (networkIp.IsIPv4MappedToIPv6)
        {
            networkIp = networkIp.MapToIPv4();
        }

        if (clientIp.AddressFamily != networkIp.AddressFamily)
        {
            return false;
        }

        var clientBytes = clientIp.GetAddressBytes();
        var networkBytes = networkIp.GetAddressBytes();

        var maxBits = clientBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > maxBits)
        {
            return false;
        }

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (clientBytes[i] != networkBytes[i])
            {
                return false;
            }
        }

        if (remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((clientBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsWildcardHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var trimmed = host.Trim();
        return string.Equals(trimmed, "*", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase);
    }

    private List<RemotePathMapping> GetMatchingCandidates(string host, bool isLocalToRemote)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return new List<RemotePathMapping>();
        }

        var rawHost = host.Trim();
        var cleanHost = ExtractIpOrHostname(rawHost);
        var resolvedHost = _callerHostResolver?.TryResolveHostname(cleanHost);

        lock (_lock)
        {
            var scoredList = new List<(RemotePathMapping Mapping, int Priority, int PrefixLength)>();

            foreach (var m in _mappings)
            {
                if (string.IsNullOrWhiteSpace(m.Host) ||
                    string.IsNullOrWhiteSpace(m.RemotePath) ||
                    string.IsNullOrWhiteSpace(m.LocalPath))
                {
                    continue;
                }

                var ruleHost = m.Host.Trim();
                var ruleHostClean = ExtractIpOrHostname(ruleHost);
                var prefixLength = (isLocalToRemote ? m.LocalPath : m.RemotePath).TrimEnd('/', '\\').Length;

                // Priority 1: Exact Hostname / IP (case-insensitive)
                if (string.Equals(ruleHost, rawHost, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ruleHostClean, cleanHost, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(resolvedHost) && (
                        string.Equals(ruleHost, resolvedHost, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ruleHostClean, resolvedHost, StringComparison.OrdinalIgnoreCase))))
                {
                    scoredList.Add((m, 300, prefixLength));
                    continue;
                }

                // Priority 2: CIDR Subnet Matching
                if (IsCidrNotation(ruleHost) && IsIpInCidr(cleanHost, ruleHost))
                {
                    var parts = ruleHost.Split('/');
                    var cidrPrefixLen = int.TryParse(parts[1], out var parsed) ? parsed : 0;
                    scoredList.Add((m, 200 + cidrPrefixLen, prefixLength));
                    continue;
                }

                // Priority 3: Wildcard Fallback (*, default, all)
                if (IsWildcardHost(ruleHost))
                {
                    scoredList.Add((m, 100, prefixLength));
                }
            }

            return scoredList
                .OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.PrefixLength)
                .ThenBy(x => x.Mapping.Id)
                .Select(x => x.Mapping)
                .ToList();
        }
    }

    public static bool IsPathPrefixMatch(string fullPath, string prefix, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        var normFullPath = NormalizeSeparators(fullPath);
        var normPrefix = NormalizeSeparators(prefix);

        var cleanPrefix = normPrefix.Length > 1 && (normPrefix.EndsWith('/') || normPrefix.EndsWith('\\'))
            ? normPrefix.TrimEnd('/', '\\')
            : normPrefix;

        var cleanFullPath = normFullPath.Length > 1 && (normFullPath.EndsWith('/') || normFullPath.EndsWith('\\'))
            ? normFullPath.TrimEnd('/', '\\')
            : normFullPath;

        if (string.Equals(cleanFullPath, cleanPrefix, comparison))
        {
            return true;
        }

        if (cleanFullPath.Length < cleanPrefix.Length)
        {
            return false;
        }

        if (!cleanFullPath.StartsWith(cleanPrefix, comparison))
        {
            return false;
        }

        if (cleanFullPath.Length == cleanPrefix.Length)
        {
            return true;
        }

        return cleanFullPath[cleanPrefix.Length] == '/' || cleanFullPath[cleanPrefix.Length] == '\\';
    }

    public static StringComparison GetPathComparison(string path)
    {
        if (IsWindowsOrigin(path))
        {
            return StringComparison.OrdinalIgnoreCase;
        }

        return StringComparison.Ordinal;
    }

    public static bool IsWindowsOrigin(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim();

        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return true;
        }

        if (trimmed.StartsWith(@"\\") || (trimmed.StartsWith("//") && !trimmed.StartsWith("///")))
        {
            return true;
        }

        return false;
    }

    public static bool IsWindowsTarget(string targetPrefix)
    {
        if (string.IsNullOrWhiteSpace(targetPrefix))
        {
            return false;
        }

        var trimmed = targetPrefix.Trim();

        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return true;
        }

        if (trimmed.StartsWith(@"\\") || trimmed.StartsWith("//"))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        return path.Replace('\\', '/');
    }

    private static string TranslatePath(string path, string sourcePrefix, string targetPrefix)
    {
        var normFullPath = NormalizeSeparators(path);
        var normSource = NormalizeSeparators(sourcePrefix);

        var cleanSource = normSource.Length > 1 && (normSource.EndsWith('/') || normSource.EndsWith('\\'))
            ? normSource.TrimEnd('/', '\\')
            : normSource;

        var cleanFullPath = normFullPath.Length > 1 && (normFullPath.EndsWith('/') || normFullPath.EndsWith('\\'))
            ? normFullPath.TrimEnd('/', '\\')
            : normFullPath;

        var relative = string.Empty;
        if (!string.Equals(cleanFullPath, cleanSource, GetPathComparison(path)))
        {
            if (normFullPath.Length > cleanSource.Length)
            {
                relative = normFullPath.Substring(cleanSource.Length).TrimStart('/');
            }
        }

        var isWindowsTarget = IsWindowsTarget(targetPrefix);
        var targetSep = isWindowsTarget ? '\\' : '/';
        string targetClean;

        if (isWindowsTarget)
        {
            var trimmedTarget = targetPrefix.Trim();
            if (trimmedTarget.StartsWith(@"\\") || trimmedTarget.StartsWith("//"))
            {
                var uncTail = trimmedTarget.Substring(2).Replace('/', '\\').TrimEnd('\\');
                targetClean = @"\\" + uncTail;
            }
            else
            {
                targetClean = trimmedTarget.Replace('/', '\\').TrimEnd('\\');
            }
        }
        else
        {
            var trimmedTarget = targetPrefix.Trim();
            var pClean = trimmedTarget.Replace('\\', '/').TrimEnd('/');
            targetClean = string.IsNullOrEmpty(pClean) ? "/" : pClean;
        }

        var relativeClean = targetSep == '\\'
            ? relative.Replace('/', '\\')
            : relative.Replace('\\', '/');

        string mappedPath;
        if (string.IsNullOrEmpty(relativeClean))
        {
            if (targetClean.Length == 2 && char.IsLetter(targetClean[0]) && targetClean[1] == ':')
            {
                mappedPath = targetClean + "\\";
            }
            else
            {
                mappedPath = targetClean;
            }
        }
        else
        {
            if (targetClean == "/" || targetClean == "\\")
            {
                mappedPath = $"{targetClean}{relativeClean}";
            }
            else
            {
                mappedPath = $"{targetClean}{targetSep}{relativeClean}";
            }
        }

        if ((path.EndsWith('/') || path.EndsWith('\\')) && !mappedPath.EndsWith(targetSep))
        {
            mappedPath += targetSep;
        }

        return mappedPath;
    }

    private static bool CheckPathExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path) || File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
