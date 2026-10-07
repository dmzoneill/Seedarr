// Copyright (c) FeedItOut. All rights reserved.

using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Terminal;

namespace NzbDrone.Core.Test.Terminal;

[TestFixture]
public class TerminalUtf8ChunkDecoderTest
{
    [Test]
    public void Decode_split_multibyte_sequence_across_chunks_emits_complete_text()
    {
        var bytes = Encoding.UTF8.GetBytes("你好");
        var decoder = new TerminalUtf8ChunkDecoder();

        var first = decoder.Decode(bytes.AsSpan(0, 2));
        Assert.That(first, Is.Empty);

        var second = decoder.Decode(bytes.AsSpan(2, 1));
        Assert.That(second, Is.EqualTo("你"));

        var rest = decoder.Decode(bytes.AsSpan(3));
        Assert.That(first + second + rest, Is.EqualTo("你好"));
    }

    [Test]
    public void Decode_split_four_byte_emoji_across_chunks()
    {
        var bytes = Encoding.UTF8.GetBytes("🎉");
        var decoder = new TerminalUtf8ChunkDecoder();

        var part1 = decoder.Decode(bytes.AsSpan(0, 2));
        var part2 = decoder.Decode(bytes.AsSpan(2, 1));
        var part3 = decoder.Decode(bytes.AsSpan(3));

        Assert.That(part1 + part2 + part3, Is.EqualTo("🎉"));
    }

    [Test]
    public void Flush_emits_trailing_incomplete_sequence_as_replacement()
    {
        var decoder = new TerminalUtf8ChunkDecoder();
        Assert.That(decoder.Decode(new byte[] { 0xE4, 0xBD }), Is.Empty);
        Assert.That(decoder.Flush(), Is.EqualTo("\uFFFD"));
    }
}
