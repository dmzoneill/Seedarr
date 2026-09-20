using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.GeoIp;

public class GeoIpService : IGeoIpService, IDisposable
{
    private const int MaxCacheEntries = 10000;

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;
    private readonly object _pathLock = new();
    private readonly ReaderWriterLockSlim _readerLock = new(LockRecursionPolicy.NoRecursion);
    private readonly ConcurrentDictionary<string, GeoLocationInfo> _ipCache = new(StringComparer.OrdinalIgnoreCase);

    private DatabaseReader _reader;
    private volatile string _resolvedDatabasePath;
    private volatile bool _disposed;

    public GeoIpService(IAppFolderInfo appFolderInfo = null)
    {
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public int CacheCount => _ipCache.Count;

    public void ClearCache()
    {
        _ipCache.Clear();
        _resolvedDatabasePath = null;
    }

    public void SetDatabasePath(string path)
    {
        lock (_pathLock)
        {
            _resolvedDatabasePath = path;
        }
    }

    public string GetDatabasePath(bool refresh = false)
    {
        if (!refresh && _resolvedDatabasePath != null)
        {
            return _resolvedDatabasePath;
        }

        lock (_pathLock)
        {
            if (!refresh && _resolvedDatabasePath != null)
            {
                return _resolvedDatabasePath;
            }

            var databaseNames = new[]
            {
                "GeoLite2-City.mmdb",
                "GeoLite2-Country.mmdb",
                "GeoIP2-City.mmdb",
                "GeoIP2-Country.mmdb",
            };

            var searchDirectories = new List<string>
            {
                "/config/GeoIP",
                "/config",
                _appFolderInfo != null ? Path.Combine(_appFolderInfo.AppDataFolder, "GeoIP") : null,
                _appFolderInfo?.AppDataFolder,
                _appFolderInfo != null ? Path.Combine(_appFolderInfo.StartUpFolder, "GeoIP") : null,
                AppDomain.CurrentDomain.BaseDirectory,
            };

            foreach (var dbName in databaseNames)
            {
                foreach (var dir in searchDirectories)
                {
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        continue;
                    }

                    var fullPath = Path.Combine(dir, dbName);
                    if (File.Exists(fullPath))
                    {
                        _resolvedDatabasePath = fullPath;
                        return _resolvedDatabasePath;
                    }
                }
            }

            _resolvedDatabasePath = null;
            return null;
        }
    }

    public GeoLocationInfo Lookup(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        if (_ipCache.TryGetValue(ipAddress, out var cached))
        {
            return cached;
        }

        if (!IPAddress.TryParse(ipAddress, out var parsedIp))
        {
            var invalidIpResult = new GeoLocationInfo { IpAddress = ipAddress };
            _ipCache.TryAdd(ipAddress, invalidIpResult);
            return invalidIpResult;
        }

        if (IsPrivateOrLoopback(parsedIp))
        {
            var localResult = new GeoLocationInfo
            {
                IpAddress = ipAddress,
                CountryCode = "LAN",
                CountryName = "Local Network",
                City = "Localhost",
            };
            _ipCache.TryAdd(ipAddress, localResult);
            return localResult;
        }

        if (_disposed)
        {
            return new GeoLocationInfo { IpAddress = ipAddress };
        }

        var dbPath = GetDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            var noDbResult = new GeoLocationInfo { IpAddress = ipAddress };
            _ipCache.TryAdd(ipAddress, noDbResult);
            return noDbResult;
        }

        var reader = EnsureReader(dbPath);
        if (reader == null)
        {
            var noReaderResult = new GeoLocationInfo { IpAddress = ipAddress };
            _ipCache.TryAdd(ipAddress, noReaderResult);
            return noReaderResult;
        }

        var lookupResult = new GeoLocationInfo { IpAddress = ipAddress };
        bool lockAcquired = false;

        try
        {
            try
            {
                _readerLock.EnterReadLock();
                lockAcquired = true;
            }
            catch (ObjectDisposedException)
            {
                return lookupResult;
            }

            if (_disposed || _reader == null)
            {
                return lookupResult;
            }

            try
            {
                if (_reader.TryCity(parsedIp, out var city))
                {
                    lookupResult = new GeoLocationInfo
                    {
                        IpAddress = ipAddress,
                        CountryCode = city.Country?.IsoCode ?? string.Empty,
                        CountryName = city.Country?.Name ?? string.Empty,
                        City = city.City?.Name ?? string.Empty,
                        Region = city.MostSpecificSubdivision?.Name ?? string.Empty,
                        Latitude = city.Location?.Latitude,
                        Longitude = city.Location?.Longitude,
                        TimeZone = city.Location?.TimeZone ?? string.Empty,
                    };
                }
                else if (_reader.TryCountry(parsedIp, out var country))
                {
                    lookupResult = new GeoLocationInfo
                    {
                        IpAddress = ipAddress,
                        CountryCode = country.Country?.IsoCode ?? string.Empty,
                        CountryName = country.Country?.Name ?? string.Empty,
                    };
                }
            }
            catch (InvalidOperationException)
            {
                // MMDB is a Country database rather than City
                if (_reader.TryCountry(parsedIp, out var country))
                {
                    lookupResult = new GeoLocationInfo
                    {
                        IpAddress = ipAddress,
                        CountryCode = country.Country?.IsoCode ?? string.Empty,
                        CountryName = country.Country?.Name ?? string.Empty,
                    };
                }
            }
        }
        catch (ObjectDisposedException)
        {
            return lookupResult;
        }
        catch (AddressNotFoundException)
        {
            // Expected when IP is not in database
        }
        catch (GeoIP2Exception ex)
        {
            _logger.Debug(ex, "MaxMind GeoIP lookup exception for IP: {0}", ipAddress);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Unexpected error during MaxMind GeoIP lookup for IP: {0}", ipAddress);
        }
        finally
        {
            if (lockAcquired)
            {
                _readerLock.ExitReadLock();
            }
        }

        if (_ipCache.Count >= MaxCacheEntries)
        {
            _ipCache.Clear();
        }

        _ipCache.TryAdd(ipAddress, lookupResult);
        return lookupResult;
    }

    public Task<GeoLocationInfo> LookupAsync(string ipAddress)
    {
        return Task.FromResult(Lookup(ipAddress));
    }

    internal static bool IsPrivateOrLoopback(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        return IPAddress.TryParse(ipAddress, out var ip) && IsPrivateOrLoopback(ip);
    }

    internal static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (ip == null)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (!ip.TryWriteBytes(bytes, out _))
            {
                return false;
            }

            // RFC 1918: 10.0.0.0/8 (10.0.0.0 - 10.255.255.255)
            if (bytes[0] == 10)
            {
                return true;
            }

            // RFC 6598: Carrier-Grade NAT (CGNAT) 100.64.0.0/10 (100.64.0.0 - 100.127.255.255)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)
            {
                return true;
            }

            // RFC 1918: 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // RFC 1918: 192.168.0.0/16 (192.168.0.0 - 192.168.255.255)
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // RFC 1122: Loopback 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return true;
            }

            // RFC 3927: Link-Local 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // RFC 1122: Current network (default route / broadcast source) 0.0.0.0/8
            if (bytes[0] == 0)
            {
                return true;
            }

            // RFC 5737: Documentation (TEST-NET-1, TEST-NET-2, TEST-NET-3)
            // 192.0.2.0/24
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            {
                return true;
            }

            // 198.51.100.0/24
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            {
                return true;
            }

            // 203.0.113.0/24
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
            {
                return true;
            }

            // RFC 2544: Benchmarking 198.18.0.0/15 (198.18.0.0 - 198.19.255.255)
            if (bytes[0] == 198 && (bytes[1] & 0xFE) == 18)
            {
                return true;
            }

            // RFC 5771: Multicast 224.0.0.0/4 (224.0.0.0 - 239.255.255.255)
            if (bytes[0] >= 224 && bytes[0] <= 239)
            {
                return true;
            }

            // RFC 1112: Reserved for future use / broadcast 240.0.0.0/4 (includes 255.255.255.255)
            if (bytes[0] >= 240)
            {
                return true;
            }
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any))
            {
                return true;
            }

            Span<byte> bytes = stackalloc byte[16];
            if (!ip.TryWriteBytes(bytes, out _))
            {
                return false;
            }

            // RFC 4291: Link-local fe80::/10
            if (ip.IsIPv6LinkLocal || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80))
            {
                return true;
            }

            // RFC 3879: Site-local fec0::/10 (deprecated)
            if (ip.IsIPv6SiteLocal)
            {
                return true;
            }

            // RFC 4193: Unique Local Address (ULA) fc00::/7 (fc00::/8, fd00::/8)
            if (ip.IsIPv6UniqueLocal || (bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            // RFC 4291: Multicast ff00::/8
            if (ip.IsIPv6Multicast || bytes[0] == 0xFF)
            {
                return true;
            }
        }

        return false;
    }

    private DatabaseReader EnsureReader(string dbPath)
    {
        if (string.IsNullOrEmpty(dbPath) || _disposed)
        {
            return null;
        }

        if (_reader != null && string.Equals(_resolvedDatabasePath, dbPath, StringComparison.OrdinalIgnoreCase))
        {
            return _reader;
        }

        bool lockAcquired = false;
        try
        {
            _readerLock.EnterWriteLock();
            lockAcquired = true;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        try
        {
            if (_disposed)
            {
                return null;
            }

            if (_reader != null && string.Equals(_resolvedDatabasePath, dbPath, StringComparison.OrdinalIgnoreCase))
            {
                return _reader;
            }

            var oldReader = _reader;
            try
            {
                _reader = new DatabaseReader(dbPath, FileAccessMode.MemoryMapped);
                _resolvedDatabasePath = dbPath;
                _ipCache.Clear();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open MaxMind database at: {0}", dbPath);
                return null;
            }

            oldReader?.Dispose();
            return _reader;
        }
        finally
        {
            if (lockAcquired)
            {
                _readerLock.ExitWriteLock();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        bool lockAcquired = false;
        try
        {
            _readerLock.EnterWriteLock();
            lockAcquired = true;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reader?.Dispose();
            _reader = null;
            _ipCache.Clear();
        }
        finally
        {
            if (lockAcquired)
            {
                _readerLock.ExitWriteLock();
            }
        }

        _readerLock.Dispose();
    }
}
