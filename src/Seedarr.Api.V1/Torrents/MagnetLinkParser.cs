using System;
using System.Linq;
using System.Web;

namespace Seedarr.Api.V1.Torrents;

public record ParsedMagnetLink(string InfoHash, string Name, string[] Trackers);

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

        var xt = parameters["xt"];
        if (string.IsNullOrEmpty(xt) || !xt.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid magnet link: missing urn:btih: parameter");
        }

        var infoHash = xt["urn:btih:".Length..];

        if (infoHash.Length == 32)
        {
            var bytes = Base32Decode(infoHash);
            if (bytes == null || bytes.Length != 20)
            {
                throw new ArgumentException("Invalid magnet link: could not decode base32 info hash");
            }

            infoHash = Convert.ToHexString(bytes).ToLowerInvariant();
        }
        else
        {
            infoHash = infoHash.ToLowerInvariant();
        }

        if (infoHash.Length != 40)
        {
            throw new ArgumentException("Invalid magnet link: info hash must be 40 hex characters");
        }

        var displayName = parameters["dn"];
        displayName = !string.IsNullOrEmpty(displayName)
            ? HttpUtility.UrlDecode(displayName)
            : infoHash;

        var rawTrackers = parameters.GetValues("tr");
        var trackers = rawTrackers?.Select(HttpUtility.UrlDecode).ToArray() ?? Array.Empty<string>();

        return new ParsedMagnetLink(infoHash, displayName, trackers);
    }

    public static byte[] Base32Decode(string input)
    {
        input = input.ToUpperInvariant();
        var output = new byte[input.Length * 5 / 8];
        var bitIndex = 0;
        var inputIndex = 0;
        var outputBits = 0;
        var outputIndex = 0;

        while (inputIndex < input.Length)
        {
            var byteIndex = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(input[inputIndex]);
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
