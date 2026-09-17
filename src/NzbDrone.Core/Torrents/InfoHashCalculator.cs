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
}
