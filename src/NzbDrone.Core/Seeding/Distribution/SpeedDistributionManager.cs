using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Seeding.Distribution;

public interface ISpeedDistributionManager
{
    long[] DistributeSpeeds(int torrentCount);
    long[] DistributeSpeeds(int torrentCount, long maxUploadSpeed);
    List<string> GetAvailableDistributions();
    string CurrentDistribution { get; }
}

public class SpeedDistributionManager : ISpeedDistributionManager
{
    private const long DefaultBytesPerSecond = 1_048_576;

    private readonly IEnumerable<ISpeedDistributor> _distributors;
    private readonly string _currentDistribution = "Equal";

    public string CurrentDistribution => _currentDistribution;

    public SpeedDistributionManager(IEnumerable<ISpeedDistributor> distributors)
    {
        _distributors = distributors;
    }

    public long[] DistributeSpeeds(int torrentCount)
    {
        return DistributeSpeeds(torrentCount, 0);
    }

    public long[] DistributeSpeeds(int torrentCount, long maxUploadSpeed)
    {
        var distributor = _distributors.FirstOrDefault(d => d.Name == _currentDistribution)
                          ?? _distributors.First();

        var effectiveSpeed = maxUploadSpeed > 0 ? maxUploadSpeed : DefaultBytesPerSecond;
        return distributor.Distribute(effectiveSpeed, torrentCount);
    }

    public List<string> GetAvailableDistributions()
    {
        return _distributors.Select(d => d.Name).ToList();
    }
}
