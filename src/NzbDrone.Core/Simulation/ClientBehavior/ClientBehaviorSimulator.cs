using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public interface IClientBehaviorSimulator
{
    IClientProfile GetActiveProfile();
    bool IsEnabled { get; }
}

public class ClientBehaviorSimulator : IClientBehaviorSimulator
{
    private readonly IConfigService _configService;
    private readonly IClientProfileFactory _profileFactory;
    private readonly Logger _logger;
    private readonly Random _random;
    private readonly object _lock = new object();

    private IClientProfile _currentProfile;

    public ClientBehaviorSimulator(IConfigService configService, IClientProfileFactory profileFactory)
    {
        _configService = configService;
        _profileFactory = profileFactory;
        _logger = LogManager.GetCurrentClassLogger();
        _random = new Random();
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
                _currentProfile = ResolveProfileByName(_configService.PrimaryClient);
                _logger.Debug("Initialized client profile: {0}", _currentProfile.Name);
            }

            if (_configService.ClientProfileSwitching)
            {
                var switchProbability = _configService.SwitchClientProbability;

                if (_random.NextDouble() < switchProbability)
                {
                    var previous = _currentProfile;
                    _currentProfile = SelectRandomAlternateProfile(_currentProfile);
                    _logger.Debug("Switched client profile from {0} to {1}", previous.Name, _currentProfile.Name);
                }
            }

            return _currentProfile;
        }
    }

    private IClientProfile GetDefaultProfile()
    {
        return ResolveProfileByName(_configService.PrimaryClient);
    }

    private IClientProfile ResolveProfileByName(string clientName)
    {
        var available = _profileFactory.GetAvailableProviders();

        if (available.Count == 0)
        {
            _logger.Warn("No client profiles available");
            return null;
        }

        var match = available.FirstOrDefault(p =>
            p.Name.StartsWith(clientName, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            return match;
        }

        _logger.Warn("No client profile matching '{0}', using first available: {1}", clientName, available[0].Name);
        return available[0];
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
