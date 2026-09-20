using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using NzbDrone.Core.WebSeeds;

namespace NzbDrone.Core.Torrents;

public record ParsedMagnetLink(string InfoHash, string Name, string[] Trackers, string InfoHashV2 = null)
{
    public List<string> WebSeeds { get; set; } = new();
    public List<string> UrlList
    {
        get => WebSeeds;
        set => WebSeeds = value;
    }
}

public static class MagnetLinkParser
{
    public static ParsedMagnetLink Parse(string magnetUri)
    {
        var queryStart = magnetUri.IndexOf('?');
        if (queryStart < 0)
        {
            throw new ArgumentException("Invalid magnet link: no parameters found");
        }

        var queryString = magnetUri[(queryStart + 1)..];
        var parameters = HttpUtility.ParseQueryString(queryString);

        var xtValues = parameters.GetValues("xt") ?? (parameters["xt"] != null ? new[] { parameters["xt"] } : Array.Empty<string>());
        if (xtValues.Length == 0)
        {
            throw new ArgumentException("Invalid magnet link: missing xt parameter");
        }

        string infoHashV1 = null;
        string infoHashV2 = null;

        foreach (var xt in xtValues)
        {
            if (string.IsNullOrWhiteSpace(xt))
            {
                continue;
            }

            if (xt.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
            {
                var rawHash = xt["urn:btih:".Length..].Trim();

                string decoded;
                var unpadded = rawHash.TrimEnd('=');
                if (unpadded.Length == 32)
                {
                    var bytes = Base32Decode(rawHash);
                    if (bytes == null || bytes.Length != 20)
                    {
                        throw new ArgumentException("Invalid magnet link: could not decode base32 info hash");
                    }

                    decoded = Convert.ToHexString(bytes).ToLowerInvariant();
                }
                else
                {
                    decoded = rawHash.ToLowerInvariant();
                }

                if (decoded.Length != 40 || !decoded.All(Uri.IsHexDigit))
                {
                    throw new ArgumentException("Invalid magnet link: info hash must be 40 valid hexadecimal characters");
                }

                infoHashV1 ??= decoded;
            }
            else if (xt.StartsWith("urn:btmh:", StringComparison.OrdinalIgnoreCase))
            {
                var rawHash = xt["urn:btmh:".Length..].Trim();
                if (!rawHash.StartsWith("1220", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Invalid magnet link: unsupported multihash prefix (expected 1220 for SHA2-256)");
                }

                var hash = rawHash[4..].ToLowerInvariant();
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                {
                    throw new ArgumentException("Invalid magnet link: v2 info hash must be 64 valid hexadecimal characters");
                }

                infoHashV2 ??= hash;
            }
        }

        if (string.IsNullOrEmpty(infoHashV1) && string.IsNullOrEmpty(infoHashV2))
        {
            throw new ArgumentException("Invalid magnet link: missing urn:btih: or urn:btmh: parameter");
        }

        var displayName = parameters["dn"];
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = infoHashV1 ?? infoHashV2;
        }

        var rawTrackers = parameters.GetValues("tr");
        var trackers = rawTrackers?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray() ?? Array.Empty<string>();

        var rawWebSeeds = parameters.GetValues("ws");
        var webSeeds = new List<string>();
        if (rawWebSeeds != null)
        {
            foreach (var rawWs in rawWebSeeds)
            {
                if (WebSeedUrlResolver.IsValidWebSeedUrl(rawWs, out var validUrl) &&
                    !webSeeds.Contains(validUrl, StringComparer.OrdinalIgnoreCase))
                {
                    webSeeds.Add(validUrl);
                }
            }
        }

        return new ParsedMagnetLink(infoHashV1, displayName, trackers, infoHashV2)
        {
            WebSeeds = webSeeds
        };
    }

    public static string BuildMagnetLink(string infoHash, string name = null, IEnumerable<string> trackers = null)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || infoHash.Length != 40 || !infoHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Invalid info hash: must be 40 valid hexadecimal characters", nameof(infoHash));
        }

        var sb = new StringBuilder();
        sb.Append("magnet:?xt=urn:btih:");
        sb.Append(infoHash.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(name))
        {
            sb.Append("&dn=");
            sb.Append(Uri.EscapeDataString(name));
        }

        if (trackers != null)
        {
            foreach (var tracker in trackers)
            {
                if (!string.IsNullOrWhiteSpace(tracker))
                {
                    sb.Append("&tr=");
                    sb.Append(Uri.EscapeDataString(tracker));
                }
            }
        }

        return sb.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        input = input.Trim().TrimEnd('=').ToUpperInvariant();
        if (input.Length == 0 || (input.Length % 8 != 0 && input.Length % 8 != 2 && input.Length % 8 != 4 && input.Length % 8 != 5 && input.Length % 8 != 7))
        {
            return null;
        }

        const string base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        if (input.Any(c => !base32Alphabet.Contains(c)))
        {
            return null;
        }

        var output = new byte[input.Length * 5 / 8];
        var bitIndex = 0;
        var inputIndex = 0;
        var outputBits = 0;
        var outputIndex = 0;

        while (inputIndex < input.Length)
        {
            var byteIndex = base32Alphabet.IndexOf(input[inputIndex]);
            if (byteIndex < 0)
            {
                return null;
            }

            var bits = Math.Min(5, 8 - bitIndex);
            if (bitIndex == 0)
            {
                outputBits = byteIndex << 3;
            }
            else if (bits < 5)
            {
                outputBits |= byteIndex >> (5 - bits);
                output[outputIndex++] = (byte)outputBits;
                outputBits = (byteIndex << (3 + bits)) & 0xFF;
            }
            else
            {
                outputBits |= byteIndex << (8 - bitIndex - 5);
            }

            bitIndex += 5;
            if (bitIndex >= 8)
            {
                bitIndex -= 8;
                if (bitIndex == 0)
                {
                    output[outputIndex++] = (byte)outputBits;
                    outputBits = 0;
                }
            }

            inputIndex++;
        }

        return output;
    }
}
