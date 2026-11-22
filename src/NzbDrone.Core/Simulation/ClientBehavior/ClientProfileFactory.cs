using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public interface IClientProfileFactory : IProviderFactory<IClientProfile, ClientProfileDefinition>
{
}

public class ClientProfileFactory : ProviderFactory<IClientProfile, ClientProfileDefinition>, IClientProfileFactory
{
    private readonly Logger _logger;
    private readonly object _seedLock = new();
    private bool _defaultsEnsured;

    public ClientProfileFactory(
        IClientProfileRepository providerRepository,
        IServiceFactory serviceFactory)
        : base(providerRepository, serviceFactory)
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public override List<ClientProfileDefinition> GetDefaultDefinitions()
    {
        return BuiltInClientProfiles.DefaultDefinitions();
    }

    public override List<IClientProfile> GetAvailableProviders()
    {
        List<ClientProfileDefinition> definitions;
        try
        {
            EnsureDefaultProfiles();
            definitions = All() ?? new List<ClientProfileDefinition>();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Client profile definitions are unavailable; using built-in profiles");
            definitions = new List<ClientProfileDefinition>();
        }

        var enabled = definitions.Where(d => d != null && d.Enable).ToList();
        var allowAllBuiltIns = definitions.Count == 0;

        List<IClientProfile> resolved;
        try
        {
            resolved = BuildAllProviders();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to resolve IClientProfile implementations from the container");
            resolved = new List<IClientProfile>();
        }

        var result = new List<IClientProfile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in resolved)
        {
            if (!IsEnabled(profile, enabled, allowAllBuiltIns))
            {
                continue;
            }

            if (seen.Add(profile.GetType().Name))
            {
                result.Add(profile);
            }
        }

        foreach (var profile in BuiltInClientProfiles.All)
        {
            if (!IsEnabled(profile, enabled, allowAllBuiltIns))
            {
                continue;
            }

            if (seen.Add(profile.GetType().Name))
            {
                result.Add(profile);
            }
        }

        return result;
    }

    private void EnsureDefaultProfiles()
    {
        if (_defaultsEnsured)
        {
            return;
        }

        lock (_seedLock)
        {
            if (_defaultsEnsured)
            {
                return;
            }

            var existing = All();
            if (existing == null || existing.Count == 0)
            {
                foreach (var definition in BuiltInClientProfiles.DefaultDefinitions())
                {
                    try
                    {
                        Create(definition);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to seed client profile definition {0}", definition.Name);
                    }
                }
            }

            _defaultsEnsured = true;
        }
    }

    private static bool IsEnabled(IClientProfile profile, List<ClientProfileDefinition> enabled, bool allowAllBuiltIns)
    {
        if (profile == null)
        {
            return false;
        }

        if (allowAllBuiltIns)
        {
            return true;
        }

        return enabled.Any(definition =>
            string.Equals(definition.Implementation, profile.GetType().Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(definition.Implementation, profile.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(definition.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
    }
}
