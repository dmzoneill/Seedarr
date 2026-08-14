using System;
using NLog;
using NzbDrone.Core.Configuration;

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
    double GetSpeedMultiplier(SeedingProfile profile, int peerCount);
}

internal enum TrafficState
{
    Normal,
    Burst,
    Idle
}

public class TrafficPatternSimulator : ITrafficPatternSimulator
{
    // Burst parameters
    private const double BurstMinMultiplier = 2.0;
    private const double BurstMaxMultiplier = 5.0;
    private const int BurstMinDurationSeconds = 5;
    private const int BurstMaxDurationSeconds = 30;
    private const double BurstProbability = 0.05;

    // Idle parameters
    private const double IdleMinMultiplier = 0.1;
    private const double IdleMaxMultiplier = 0.3;
    private const int IdleMinDurationSeconds = 10;
    private const int IdleMaxDurationSeconds = 120;
    private const double IdleProbability = 0.08;

    // Network congestion parameters
    private const double CongestionAmplitude = 0.15;
    private const double CongestionCyclePeriodSeconds = 60.0;

    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly Random _random;
    private readonly object _lock = new object();

    private TrafficState _currentState;
    private DateTime _stateExpiresAt;
    private double _stateMultiplier;

    public TrafficPatternSimulator(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
        _random = new Random();
        _currentState = TrafficState.Normal;
        _stateExpiresAt = DateTime.MinValue;
        _stateMultiplier = 1.0;
    }

    public double GetSpeedMultiplier(SeedingProfile profile)
    {
        return GetSpeedMultiplier(profile, 0);
    }

    public double GetSpeedMultiplier(SeedingProfile profile, int peerCount)
    {
        var effectiveProfile = ResolveProfile(profile);

        var hour = DateTime.UtcNow.Hour;
        var baseMultiplier = effectiveProfile switch
        {
            SeedingProfile.Conservative => 0.5,
            SeedingProfile.Balanced => 1.0,
            SeedingProfile.Aggressive => 1.5,
            _ => 1.0
        };

        // Only apply time-of-day variation when timeBasedPatterns is enabled
        var timeMultiplier = 1.0;
        if (_configService.TimeBasedPatterns)
        {
            timeMultiplier = hour switch
            {
                >= 2 and < 6 => 1.3,
                >= 6 and < 12 => 1.0,
                >= 12 and < 18 => 0.9,
                >= 18 and < 22 => 0.7,
                _ => 1.1
            };
        }

        // Only apply realistic variations (burst/idle states, congestion) when enabled
        var stateMultiplier = 1.0;
        var congestionMultiplier = 1.0;
        if (_configService.RealisticVariations)
        {
            stateMultiplier = GetStateMultiplier();
            congestionMultiplier = GetCongestionMultiplier();
        }

        var peerMultiplier = GetPeerCountMultiplier(peerCount);

        // Apply behaviorVariation as a randomized scaling factor around 1.0
        var behaviorVariation = _configService.BehaviorVariation;
        var variationMultiplier = 1.0;
        if (behaviorVariation > 0.0 && _configService.RealisticVariations)
        {
            variationMultiplier = 1.0 + (((_random.NextDouble() * 2.0) - 1.0) * behaviorVariation);
        }

        var result = baseMultiplier * timeMultiplier * stateMultiplier * congestionMultiplier * peerMultiplier * variationMultiplier;

        _logger.Trace(
            "Speed multiplier: {0:F2} (profile={1}, hour={2}, state={3}, congestion={4:F2}, peers={5}, peerMult={6:F2}, variation={7:F2})",
            result,
            effectiveProfile,
            hour,
            _currentState,
            congestionMultiplier,
            peerCount,
            peerMultiplier,
            variationMultiplier);

        return result;
    }

    private SeedingProfile ResolveProfile(SeedingProfile fallback)
    {
        var configured = _configService.TrafficPatternProfile;

        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        if (Enum.TryParse<SeedingProfile>(configured, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        _logger.Warn("Unknown TrafficPatternProfile '{0}', falling back to {1}", configured, fallback);
        return fallback;
    }

    private double GetStateMultiplier()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            if (_currentState != TrafficState.Normal && now >= _stateExpiresAt)
            {
                _logger.Debug("Traffic state returning to Normal from {0}", _currentState);
                _currentState = TrafficState.Normal;
                _stateMultiplier = 1.0;
            }

            if (_currentState == TrafficState.Normal)
            {
                var roll = _random.NextDouble();
                if (roll < BurstProbability)
                {
                    EnterBurstState(now);
                }
                else if (roll < BurstProbability + IdleProbability)
                {
                    EnterIdleState(now);
                }
            }

            return _stateMultiplier;
        }
    }

    private void EnterBurstState(DateTime now)
    {
        _currentState = TrafficState.Burst;
        _stateMultiplier = BurstMinMultiplier + (_random.NextDouble() * (BurstMaxMultiplier - BurstMinMultiplier));
        var duration = _random.Next(BurstMinDurationSeconds, BurstMaxDurationSeconds + 1);
        _stateExpiresAt = now.AddSeconds(duration);
        _logger.Debug("Entering Burst state: multiplier={0:F2}, duration={1}s", _stateMultiplier, duration);
    }

    private void EnterIdleState(DateTime now)
    {
        _currentState = TrafficState.Idle;
        _stateMultiplier = IdleMinMultiplier + (_random.NextDouble() * (IdleMaxMultiplier - IdleMinMultiplier));
        var duration = _random.Next(IdleMinDurationSeconds, IdleMaxDurationSeconds + 1);
        _stateExpiresAt = now.AddSeconds(duration);
        _logger.Debug("Entering Idle state: multiplier={0:F2}, duration={1}s", _stateMultiplier, duration);
    }

    private double GetCongestionMultiplier()
    {
        var seconds = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        var sineValue = Math.Sin(2.0 * Math.PI * seconds / CongestionCyclePeriodSeconds);
        return 1.0 + (CongestionAmplitude * sineValue);
    }

    private static double GetPeerCountMultiplier(int peerCount)
    {
        if (peerCount <= 0)
        {
            return 1.0;
        }

        // More peers in the swarm means more bandwidth competition.
        // Scale down speed as peer count grows using a log curve:
        //   1-5 peers   -> ~1.0x (small swarm, full speed)
        //   10-20 peers -> ~0.8x (moderate competition)
        //   50+ peers   -> ~0.6x (heavy competition)
        //   200+ peers  -> ~0.45x (very crowded swarm)
        var factor = 1.0 / (1.0 + (0.15 * Math.Log(1 + peerCount)));
        return Math.Max(factor, 0.3);
    }
}
