// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Text;

namespace NzbDrone.Core.Terminal;

/// <summary>
/// Decodes incremental PTY byte chunks into UTF-16 text, retaining incomplete UTF-8 sequences between calls.
/// </summary>
public sealed class TerminalUtf8ChunkDecoder
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _charScratch = new char[128];
    private readonly StringBuilder _text = new();

    public string Decode(ReadOnlySpan<byte> bytes, bool flush = false)
    {
        _text.Clear();
        if (bytes.IsEmpty && !flush)
        {
            return string.Empty;
        }

        var remaining = bytes;
        while (!remaining.IsEmpty || flush)
        {
            _decoder.Convert(
                remaining,
                _charScratch,
                flush,
                out int bytesUsed,
                out int charsUsed,
                out bool completed);

            if (charsUsed > 0)
            {
                _text.Append(_charScratch, 0, charsUsed);
            }

            if (completed || bytesUsed == 0)
            {
                break;
            }

            remaining = remaining.Slice(bytesUsed);
            flush = false;
        }

        return _text.ToString();
    }

    public string Flush()
    {
        return Decode(ReadOnlySpan<byte>.Empty, flush: true);
    }
}
