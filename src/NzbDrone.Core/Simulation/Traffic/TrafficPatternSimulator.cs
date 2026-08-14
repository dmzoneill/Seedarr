using System;
using NLog;

namespace NzbDrone.Core.Simulation.Traffic;

public enum SeedingProfile
{
    Conservative,
    Balanced,
    Aggressive
}

public interface ITrafficPatternSimulator
{
    double GetSpeedMultiplier(SeedingProfile profile);
}

public class TrafficPatternSimulator : ITrafficPatternSimulator
{
    private readonly Logger _logger;

    public TrafficPatternSimulator()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public double GetSpeedMultiplier(SeedingProfile profile)
    {
        var hour = DateTime.UtcNow.Hour;
        var baseMultiplier = profile switch
        {
            SeedingProfile.Conservative => 0.5,
            SeedingProfile.Balanced => 1.0,
            SeedingProfile.Aggressive => 1.5,
            _ => 1.0
        };

        // Simulate time-of-day variation: lower at peak hours (18-22), higher at off-peak (2-6)
        var timeMultiplier = hour switch
        {
            >= 2 and < 6 => 1.3,
            >= 6 and < 12 => 1.0,
            >= 12 and < 18 => 0.9,
            >= 18 and < 22 => 0.7,
            _ => 1.1
        };

        var result = baseMultiplier * timeMultiplier;
        _logger.Trace("Speed multiplier: {0:F2} (profile={1}, hour={2})", result, profile, hour);
        return result;
    }
}
