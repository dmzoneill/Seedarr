using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Seeding.Distribution;

public interface ISpeedDistributionManager
{
    long[] DistributeSpeeds(int torrentCount);
    List<string> GetAvailableDistributions();
    string CurrentDistribution { get; }
}

public class SpeedDistributionManager : ISpeedDistributionManager
{
    private readonly IEnumerable<ISpeedDistributor> _distributors;

    private readonly long _totalBytesPerSecond = 1_048_576;
    private readonly string _currentDistribution = "Equal";

    public string CurrentDistribution => _currentDistribution;

    public SpeedDistributionManager(IEnumerable<ISpeedDistributor> distributors)
    {
        _distributors = distributors;
    }

    public long[] DistributeSpeeds(int torrentCount)
    {
        var distributor = _distributors.FirstOrDefault(d => d.Name == _currentDistribution)
                          ?? _distributors.First();

        return distributor.Distribute(_totalBytesPerSecond, torrentCount);
    }

    public List<string> GetAvailableDistributions()
    {
        return _distributors.Select(d => d.Name).ToList();
    }
}
