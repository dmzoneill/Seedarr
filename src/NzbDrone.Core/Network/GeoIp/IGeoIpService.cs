using System.Threading.Tasks;

namespace NzbDrone.Core.Network.GeoIp;

public class GeoLocationInfo
{
    public string IpAddress { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string TimeZone { get; set; } = string.Empty;
}

public interface IGeoIpService
{
    Task<GeoLocationInfo> LookupAsync(string ipAddress);
    GeoLocationInfo Lookup(string ipAddress);
}
