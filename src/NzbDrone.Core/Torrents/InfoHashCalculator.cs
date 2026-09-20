using System;
using System.Security.Cryptography;
using BencodeNET.Objects;

namespace NzbDrone.Core.Torrents;

public static class InfoHashCalculator
{
    public static string Calculate(ReadOnlySpan<byte> rawInfoBytes)
    {
        var hash = SHA1.HashData(rawInfoBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Calculate(BDictionary infoDictionary)
    {
        if (infoDictionary == null)
        {
            throw new ArgumentNullException(nameof(infoDictionary));
        }

        var encoded = infoDictionary.EncodeAsBytes();
        return Calculate(encoded);
    }

    public static string CalculateV2(ReadOnlySpan<byte> rawInfoBytes)
    {
        var hash = SHA256.HashData(rawInfoBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string CalculateV2(BDictionary infoDictionary)
    {
        if (infoDictionary == null)
        {
            throw new ArgumentNullException(nameof(infoDictionary));
        }

        var encoded = infoDictionary.EncodeAsBytes();
        return CalculateV2(encoded);
    }

    public static void Calculate(ReadOnlySpan<byte> rawInfoBytes, out string v1Hash, out string v2Hash)
    {
        v1Hash = Calculate(rawInfoBytes);
        v2Hash = CalculateV2(rawInfoBytes);
    }

    public static void Calculate(BDictionary infoDictionary, out string v1Hash, out string v2Hash)
    {
        if (infoDictionary == null)
        {
            throw new ArgumentNullException(nameof(infoDictionary));
        }

        var encoded = infoDictionary.EncodeAsBytes();
        Calculate(encoded, out v1Hash, out v2Hash);
    }
}
