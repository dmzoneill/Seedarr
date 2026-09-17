using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NzbDrone.Core.RemotePathMappings;

public class RemotePathMappingService : IRemotePathMappingService
{
    private readonly List<RemotePathMapping> _mappings = new();
    private readonly object _lock = new();
    private int _nextId = 1;

    public RemotePathMappingService(IEnumerable<RemotePathMapping> initialMappings = null)
    {
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
        if (string.IsNullOrEmpty(remotePath) || string.IsNullOrWhiteSpace(host))
        {
            return remotePath;
        }

        return TestMapping(host, remotePath, "remoteToLocal").MappedPath;
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

        List<RemotePathMapping> candidates;
        lock (_lock)
        {
            candidates = _mappings
                .Where(m => !string.IsNullOrWhiteSpace(m.Host) &&
                            !string.IsNullOrWhiteSpace(m.RemotePath) &&
                            !string.IsNullOrWhiteSpace(m.LocalPath) &&
                            string.Equals(m.Host.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => (isLocalToRemote ? m.LocalPath : m.RemotePath).TrimEnd('/', '\\').Length)
                .ToList();
        }

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
