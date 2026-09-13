using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.GeoIp;

public class GeoIpService : IGeoIpService, IDisposable
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;
    private readonly object _lock = new();

    private DatabaseReader _reader;
    private string _resolvedDatabasePath;
    private bool _disposed;

    public GeoIpService(IAppFolderInfo appFolderInfo = null)
    {
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public string GetDatabasePath()
    {
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
                    return fullPath;
                }
            }
        }

        return null;
    }

    public GeoLocationInfo Lookup(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        if (!IPAddress.TryParse(ipAddress, out var parsedIp))
        {
            return new GeoLocationInfo { IpAddress = ipAddress };
        }

        if (IsPrivateOrLoopback(parsedIp))
        {
            return new GeoLocationInfo
            {
                IpAddress = ipAddress,
                CountryCode = "LAN",
                CountryName = "Local Network",
                City = "Localhost",
            };
        }

        var dbPath = GetDatabasePath();
        if (string.IsNullOrEmpty(dbPath))
        {
            return new GeoLocationInfo { IpAddress = ipAddress };
        }

        try
        {
            var reader = EnsureReader(dbPath);
            if (reader == null)
            {
                return new GeoLocationInfo { IpAddress = ipAddress };
            }

            try
            {
                if (reader.TryCity(parsedIp, out var city))
                {
                    return new GeoLocationInfo
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
            }
            catch (InvalidOperationException)
            {
                // MMDB is a Country database rather than City
            }

            if (reader.TryCountry(parsedIp, out var country))
            {
                return new GeoLocationInfo
                {
                    IpAddress = ipAddress,
                    CountryCode = country.Country?.IsoCode ?? string.Empty,
                    CountryName = country.Country?.Name ?? string.Empty,
                };
            }
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

        return new GeoLocationInfo { IpAddress = ipAddress };
    }

    public Task<GeoLocationInfo> LookupAsync(string ipAddress)
    {
        return Task.FromResult(Lookup(ipAddress));
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
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

            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            // 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return true;
            }

            // 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // 0.0.0.0/8
            if (bytes[0] == 0)
            {
                return true;
            }
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Loopback))
            {
                return true;
            }

            Span<byte> bytes = stackalloc byte[16];
            if (!ip.TryWriteBytes(bytes, out _))
            {
                return false;
            }

            // Link-local fe80::/10
            if (ip.IsIPv6LinkLocal || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80))
            {
                return true;
            }

            // Site-local fec0::/10
            if (ip.IsIPv6SiteLocal)
            {
                return true;
            }

            // Unique Local Address fc00::/7
            if (ip.IsIPv6UniqueLocal || (bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }
        }

        return false;
    }

    private DatabaseReader EnsureReader(string dbPath)
    {
        if (_reader != null && _resolvedDatabasePath == dbPath)
        {
            return _reader;
        }

        lock (_lock)
        {
            if (_reader != null && _resolvedDatabasePath == dbPath)
            {
                return _reader;
            }

            _reader?.Dispose();
            _reader = new DatabaseReader(dbPath, FileAccessMode.MemoryMapped);
            _resolvedDatabasePath = dbPath;
            return _reader;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            lock (_lock)
            {
                _reader?.Dispose();
                _reader = null;
            }
        }
    }
}
