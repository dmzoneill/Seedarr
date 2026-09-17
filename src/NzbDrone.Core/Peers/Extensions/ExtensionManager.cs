using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Simulation.ClientBehavior;

namespace NzbDrone.Core.Peers.Extensions;

public interface IExtensionManager
{
    Dictionary<string, int> GetSupportedExtensions(bool isPrivate = false);

    byte[] BuildExtensionHandshake(
        IPAddress remoteIp = null,
        int listeningPort = 0,
        long metadataSize = 0,
        bool isPrivate = false,
        IClientProfile clientProfile = null);

    byte[] BuildExtensionHandshake(
        bool isPrivate,
        IClientProfile clientProfile = null);

    ExtensionHandshake ParseExtensionHandshake(byte[] data);

    ExtensionHandshake ParseExtensionHandshake(BDictionary dict);

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

    public byte[] BuildExtensionHandshake(
        bool isPrivate,
        IClientProfile clientProfile = null)
    {
        return BuildExtensionHandshake(null, 0, 0, isPrivate, clientProfile);
    }

    public byte[] BuildExtensionHandshake(
        IPAddress remoteIp = null,
        int listeningPort = 0,
        long metadataSize = 0,
        bool isPrivate = false,
        IClientProfile clientProfile = null)
    {
        var extensions = GetSupportedExtensions(isPrivate);
        var mDict = new BDictionary();
        foreach (var kvp in extensions)
        {
            mDict[kvp.Key] = new BNumber(kvp.Value);
        }

        var clientVersion = clientProfile?.UserAgent ?? clientProfile?.Name ?? (BuildInfo.Version != null ? $"Seedarr/{BuildInfo.Version}" : "Seedarr/1.0.0");
        var reqq = (_configService != null && _configService.PeerRequestCount > 0) ? _configService.PeerRequestCount : 250;

        var dict = new BDictionary
        {
            ["m"] = mDict,
            ["v"] = new BString(clientVersion),
            ["reqq"] = new BNumber(reqq)
        };

        if (listeningPort > 0)
        {
            dict["p"] = new BNumber(listeningPort);
        }

        if (metadataSize > 0)
        {
            dict["metadata_size"] = new BNumber(metadataSize);
        }

        if (remoteIp != null)
        {
            var ip = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
            dict["yourip"] = new BString(ip.GetAddressBytes());
        }

        return dict.EncodeAsBytes();
    }

    public ExtensionHandshake ParseExtensionHandshake(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return null;
        }

        try
        {
            var parser = new BencodeParser();
            using var stream = new MemoryStream(data);
            var dict = parser.Parse<BDictionary>(stream);
            return ParseExtensionHandshake(dict);
        }
        catch
        {
            return null;
        }
    }

    public ExtensionHandshake ParseExtensionHandshake(BDictionary dict)
    {
        if (dict == null)
        {
            return null;
        }

        var handshake = new ExtensionHandshake();

        if (dict.TryGetValue("m", out var mObj) && mObj is BDictionary mDict)
        {
            foreach (var kvp in mDict)
            {
                if (kvp.Value is BNumber num)
                {
                    handshake.Extensions[kvp.Key.ToString()] = (int)num.Value;
                }
            }
        }

        if (dict.TryGetValue("v", out var vObj) && vObj is BString vStr)
        {
            try
            {
                handshake.Version = Encoding.UTF8.GetString(vStr.Value.Span);
            }
            catch
            {
                handshake.Version = Encoding.Latin1.GetString(vStr.Value.Span);
            }
        }

        if (dict.TryGetValue("reqq", out var reqqObj) && reqqObj is BNumber reqqNum)
        {
            handshake.RequestQueueLength = (int)reqqNum.Value;
        }

        if (dict.TryGetValue("p", out var pObj) && pObj is BNumber pNum)
        {
            handshake.Port = (int)pNum.Value;
        }

        if (dict.TryGetValue("metadata_size", out var metaSizeObj) && metaSizeObj is BNumber metaSizeNum)
        {
            handshake.MetadataSize = metaSizeNum.Value;
        }

        if (dict.TryGetValue("yourip", out var yourIpObj) && yourIpObj is BString yourIpStr)
        {
            var bytes = yourIpStr.Value.ToArray();
            if (bytes.Length == 4 || bytes.Length == 16)
            {
                handshake.YourIp = new IPAddress(bytes);
            }
        }

        return handshake;
    }
}
