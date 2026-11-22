using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Simulation.ClientBehavior.Profiles;

namespace NzbDrone.Core.Simulation.ClientBehavior;

public static class BuiltInClientProfiles
{
    private static readonly IClientProfile[] Profiles =
    {
        new QBittorrentProfile(),
        new DelugeProfile(),
        new TransmissionProfile(),
        new UTorrentProfile(),
        new BiglyBTProfile()
    };

    public static IReadOnlyList<IClientProfile> All => Profiles;

    public static List<ClientProfileDefinition> DefaultDefinitions()
    {
        return Profiles
            .Select(profile => new ClientProfileDefinition
            {
                Name = profile.Name,
                Implementation = profile.GetType().Name,
                ConfigContract = profile.GetType().Name + "Settings",
                Enable = true,
                Priority = 1
            })
            .ToList();
    }
}
