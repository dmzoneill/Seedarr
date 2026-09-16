using System;
using System.Collections.Generic;
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

        List<RemotePathMapping> candidates;
        lock (_lock)
        {
            candidates = _mappings
                .Where(m => !string.IsNullOrWhiteSpace(m.Host) &&
                            !string.IsNullOrWhiteSpace(m.RemotePath) &&
                            !string.IsNullOrWhiteSpace(m.LocalPath) &&
                            string.Equals(m.Host.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.RemotePath.Length)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return remotePath;
        }

        var normRemote = remotePath.Replace('\\', '/').TrimEnd('/');

        foreach (var mapping in candidates)
        {
            var normMappingRemote = mapping.RemotePath.Replace('\\', '/').TrimEnd('/');

            var targetSep = mapping.LocalPath.Contains('\\') ? '\\' : '/';
            var localClean = mapping.LocalPath.TrimEnd('/', '\\');

            // Exact match
            if (string.Equals(normRemote, normMappingRemote, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.LocalPath;
            }

            // Subpath match
            if (normRemote.StartsWith(normMappingRemote + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = normRemote.Substring(normMappingRemote.Length).TrimStart('/');
                var relativeClean = targetSep == '/' ? relative.Replace('\\', '/') : relative.Replace('/', '\\');

                var result = string.IsNullOrEmpty(localClean)
                    ? $"{targetSep}{relativeClean}"
                    : $"{localClean}{targetSep}{relativeClean}";

                if (remotePath.EndsWith('/') || remotePath.EndsWith('\\'))
                {
                    result += targetSep;
                }

                return result;
            }
        }

        return remotePath;
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
}
