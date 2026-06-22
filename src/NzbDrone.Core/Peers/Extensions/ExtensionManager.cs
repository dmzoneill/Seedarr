using System.Collections.Generic;
using BencodeNET.Objects;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Peers.Extensions;

public interface IExtensionManager
{
    Dictionary<string, int> GetSupportedExtensions();
    byte[] BuildExtensionHandshake();
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

    public Dictionary<string, int> GetSupportedExtensions()
    {
        var extensions = new Dictionary<string, int>();
        var nextId = 1;

        if (_configService.ExtensionUtPex)
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

    public byte[] BuildExtensionHandshake()
    {
        var extensions = GetSupportedExtensions();
        var mDict = new BDictionary();
        foreach (var kvp in extensions)
        {
            mDict[kvp.Key] = new BNumber(kvp.Value);
        }

        var dict = new BDictionary
        {
            ["m"] = mDict
        };

        return dict.EncodeAsBytes();
    }
}
