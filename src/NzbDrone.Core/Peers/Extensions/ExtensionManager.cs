using System.Collections.Generic;

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
        ["ut_pex"] = 1,       // BEP 11
        ["ut_metadata"] = 2,  // BEP 9
    };

    public Dictionary<string, int> GetSupportedExtensions()
    {
        return new Dictionary<string, int>(_extensions);
    }

    public byte[] BuildExtensionHandshake()
    {
        // BEP 10: Extended handshake is a bencoded dictionary
        var dict = new BencodeNET.Objects.BDictionary
        {
            ["m"] = new BencodeNET.Objects.BDictionary
            {
                ["ut_pex"] = new BencodeNET.Objects.BNumber(1),
                ["ut_metadata"] = new BencodeNET.Objects.BNumber(2)
            }
        };

        return dict.EncodeAsBytes();
    }
}
