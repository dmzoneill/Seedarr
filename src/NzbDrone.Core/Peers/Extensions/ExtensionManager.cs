using System.Collections.Generic;
using BencodeNET.Objects;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.ClientBehavior;

namespace NzbDrone.Core.Peers.Extensions;

public interface IExtensionManager
{
    Dictionary<string, int> GetSupportedExtensions(bool isPrivate = false);
    byte[] BuildExtensionHandshake(bool isPrivate = false, IClientProfile clientProfile = null);
    bool FastExtensionEnabled { get; }
}

public class ExtensionManager : IExtensionManager
{
    private readonly IConfigService _configService;

    public ExtensionManager(IConfigService configService)
    {
        _configService = configService;
    }

    public bool FastExtensionEnabled => _configService.ExtensionFastExtension;

    public Dictionary<string, int> GetSupportedExtensions(bool isPrivate = false)
    {
        var extensions = new Dictionary<string, int>();
        var nextId = 1;

        // BEP 27: Never offer PEX for private torrent swarms
        if (_configService.ExtensionUtPex && !isPrivate)
        {
            extensions["ut_pex"] = nextId++;
        }

        if (_configService.ExtensionUtMetadata)
        {
            extensions["ut_metadata"] = nextId++;
        }

        if (_configService.ExtensionLtDontHave)
        {
            extensions["lt_donthave"] = nextId++;
        }

        return extensions;
    }

    public byte[] BuildExtensionHandshake(bool isPrivate = false, IClientProfile clientProfile = null)
    {
        var extensions = GetSupportedExtensions(isPrivate);
        var mDict = new BDictionary();
        foreach (var kvp in extensions)
        {
            mDict[kvp.Key] = new BNumber(kvp.Value);
        }

        var clientVersion = clientProfile?.UserAgent ?? clientProfile?.Name ?? (BuildInfo.Version != null ? $"Seedarr/{BuildInfo.Version}" : "Seedarr/1.0.0");

        var dict = new BDictionary
        {
            ["m"] = mDict,
            ["v"] = new BString(clientVersion)
        };

        return dict.EncodeAsBytes();
    }
}
