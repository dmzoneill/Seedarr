using System;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public interface IClientBehaviorSimulator
{
    IClientProfile GetActiveProfile();
    bool IsEnabled { get; }
    double GetEffectiveDropoutProbability(double baseProbability);
    double GetEffectiveRotationPercentage(double basePercentage);
    double GetEffectiveIdleChance(double baseIdleChance);
}

public class ClientBehaviorSimulator : IClientBehaviorSimulator
{
    private static readonly IClientProfile FallbackProfile = new QBittorrentProfile();

    private readonly IConfigService _configService;
    private readonly IClientProfileFactory _profileFactory;
    private readonly Logger _logger;
    private readonly IRandomNumberGenerator _random;
    private readonly object _lock = new object();

    private IClientProfile _currentProfile;

    public ClientBehaviorSimulator(
        IConfigService configService,
        IClientProfileFactory profileFactory,
        IRandomNumberGenerator random = null)
    {
        _configService = configService;
        _profileFactory = profileFactory;
        _logger = LogManager.GetCurrentClassLogger();
        _random = random ?? new RandomNumberGenerator();
    }

    public bool IsEnabled => _configService.ClientBehaviorEngineEnabled;

    public IClientProfile GetActiveProfile()
    {
        if (!_configService.ClientBehaviorEngineEnabled)
        {
            _logger.Trace("Client behavior engine disabled, returning default profile");
            return GetDefaultProfile();
        }

        lock (_lock)
        {
            if (_currentProfile == null)
            {
                _currentProfile = ResolveProfileByName(_configService.PrimaryClient) ?? FallbackProfile;
                _logger.Debug("Initialized client profile: {0}", _currentProfile.Name);
            }

            if (_configService.ClientProfileSwitching)
            {
                var switchProbability = _configService.SwitchClientProbability;

                if (_random.NextDouble() < switchProbability)
                {
                    var previous = _currentProfile;
                    _currentProfile = SelectRandomAlternateProfile(_currentProfile) ?? _currentProfile;
                    _logger.Debug("Switched client profile from {0} to {1}", previous?.Name, _currentProfile?.Name);
                }
            }

            return _currentProfile ?? FallbackProfile;
        }
    }

    public double GetEffectiveDropoutProbability(double baseProbability)
    {
        if (!_configService.ClientBehaviorEngineEnabled || baseProbability <= 0)
        {
            return baseProbability;
        }

        var profile = GetActiveProfile();
        var profileModifier = GetProfileDropoutModifier(profile);
        var variationModifier = GetVariationModifier();

        return Math.Clamp(baseProbability * profileModifier * variationModifier, 0.0, 1.0);
    }

    public double GetEffectiveRotationPercentage(double basePercentage)
    {
        if (!_configService.ClientBehaviorEngineEnabled || basePercentage <= 0)
        {
            return basePercentage;
        }

        var profile = GetActiveProfile();
        var profileModifier = GetProfileRotationModifier(profile);
        var variationModifier = GetVariationModifier();

        return Math.Clamp(basePercentage * profileModifier * variationModifier, 0.0, 1.0);
    }

    public double GetEffectiveIdleChance(double baseIdleChance)
    {
        if (!_configService.ClientBehaviorEngineEnabled || baseIdleChance <= 0)
        {
            return baseIdleChance;
        }

        var profile = GetActiveProfile();
        var profileModifier = GetProfileIdleModifier(profile);
        var variationModifier = GetVariationModifier();

        return Math.Clamp(baseIdleChance * profileModifier * variationModifier, 0.0, 1.0);
    }

    private double GetVariationModifier()
    {
        var variation = _configService.BehaviorVariation;
        if (variation <= 0)
        {
            return 1.0;
        }

        lock (_lock)
        {
            return 1.0 + (((_random.NextDouble() * 2.0) - 1.0) * variation);
        }
    }

    private static double GetProfileDropoutModifier(IClientProfile profile)
    {
        if (profile == null)
        {
            return 1.0;
        }

        var name = profile.Name;
        if (name.StartsWith("Transmission", StringComparison.OrdinalIgnoreCase))
        {
            return 0.8;
        }

        if (name.StartsWith("uTorrent", StringComparison.OrdinalIgnoreCase))
        {
            return 1.25;
        }

        if (name.StartsWith("Deluge", StringComparison.OrdinalIgnoreCase))
        {
            return 1.1;
        }

        if (name.StartsWith("BiglyBT", StringComparison.OrdinalIgnoreCase))
        {
            return 0.9;
        }

        return 1.0;
    }

    private static double GetProfileRotationModifier(IClientProfile profile)
    {
        if (profile == null)
        {
            return 1.0;
        }

        var name = profile.Name;
        if (name.StartsWith("Deluge", StringComparison.OrdinalIgnoreCase))
        {
            return 1.2;
        }

        if (name.StartsWith("Transmission", StringComparison.OrdinalIgnoreCase))
        {
            return 0.85;
        }

        if (name.StartsWith("uTorrent", StringComparison.OrdinalIgnoreCase))
        {
            return 1.15;
        }

        if (name.StartsWith("BiglyBT", StringComparison.OrdinalIgnoreCase))
        {
            return 0.95;
        }

        return 1.0;
    }

    private static double GetProfileIdleModifier(IClientProfile profile)
    {
        if (profile == null)
        {
            return 1.0;
        }

        var name = profile.Name;
        if (name.StartsWith("Deluge", StringComparison.OrdinalIgnoreCase))
        {
            return 0.8;
        }

        if (name.StartsWith("Transmission", StringComparison.OrdinalIgnoreCase))
        {
            return 1.1;
        }

        if (name.StartsWith("BiglyBT", StringComparison.OrdinalIgnoreCase))
        {
            return 0.85;
        }

        if (name.StartsWith("uTorrent", StringComparison.OrdinalIgnoreCase))
        {
            return 1.05;
        }

        return 1.0;
    }

    private IClientProfile GetDefaultProfile()
    {
        return ResolveProfileByName(_configService.PrimaryClient) ?? FallbackProfile;
    }

    private IClientProfile ResolveProfileByName(string clientName)
    {
        var available = _profileFactory.GetAvailableProviders();

        if (available == null || available.Count == 0)
        {
            _logger.Debug("No client profiles configured, using fallback profile: {0}", FallbackProfile.Name);
            return FallbackProfile;
        }

        if (!string.IsNullOrWhiteSpace(clientName))
        {
            var match = available.FirstOrDefault(p =>
                p.Name.StartsWith(clientName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        _logger.Warn("No client profile matching '{0}', using first available: {1}", clientName, available[0].Name);
        return available[0] ?? FallbackProfile;
    }

    private IClientProfile SelectRandomAlternateProfile(IClientProfile current)
    {
        var available = _profileFactory.GetAvailableProviders();

        if (available.Count <= 1)
        {
            return current;
        }

        var alternatives = available.Where(p => p.Name != current.Name).ToList();

        if (alternatives.Count == 0)
        {
            return current;
        }

        return alternatives[_random.Next(alternatives.Count)];
    }
}
