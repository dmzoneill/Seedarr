using System.Collections.Generic;
using BencodeNET.Objects;

namespace NzbDrone.Core.Peers.Extensions;

public interface IExtensionManager
{
    Dictionary<string, int> GetSupportedExtensions();
    byte[] BuildExtensionHandshake();
}

public class ExtensionManager : IExtensionManager
{
    private readonly Dictionary<string, int> _extensions = new()
    {
        ["ut_pex"] = 1,
        ["ut_metadata"] = 2,
    };

    public Dictionary<string, int> GetSupportedExtensions()
    {
        return new Dictionary<string, int>(_extensions);
    }

    public byte[] BuildExtensionHandshake()
    {
        var dict = new BDictionary
        {
            ["m"] = new BDictionary
            {
                ["ut_pex"] = new BNumber(1),
                ["ut_metadata"] = new BNumber(2)
            }
        };

        return dict.EncodeAsBytes();
    }
}
