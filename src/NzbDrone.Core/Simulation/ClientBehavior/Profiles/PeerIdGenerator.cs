using System;
using System.Security.Cryptography;

namespace NzbDrone.Core.Simulation.ClientBehavior.Profiles;

public static class PeerIdGenerator
{
    public const string Base62CharacterSet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string UrlSafeCharacterSet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-_";

    public static string Generate(string prefix, string characterSet, int suffixLength = 12)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(characterSet);

        if (characterSet.Length == 0 || characterSet.Length > 256)
        {
            throw new ArgumentException("Character set must contain between 1 and 256 characters.", nameof(characterSet));
        }

        var suffixBytes = RandomNumberGenerator.GetBytes(suffixLength);
        var chars = new char[suffixLength];
        var maxUnbiased = (256 / characterSet.Length) * characterSet.Length;
        Span<byte> singleByte = stackalloc byte[1];

        for (var i = 0; i < suffixLength; i++)
        {
            var b = (int)suffixBytes[i];
            while (b >= maxUnbiased)
            {
                RandomNumberGenerator.Fill(singleByte);
                b = singleByte[0];
            }

            chars[i] = characterSet[b % characterSet.Length];
        }

        return prefix + new string(chars);
    }
}
