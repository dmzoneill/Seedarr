using System;

namespace NzbDrone.Core.Peers.Encryption;

public class Rc4StreamCipher
{
    private readonly byte[] _s = new byte[256];
    private byte _i;
    private byte _j;

    public Rc4StreamCipher(ReadOnlySpan<byte> key, bool discard1024 = true)
    {
        if (key.Length == 0)
        {
            throw new ArgumentException("Key must not be empty.", nameof(key));
        }

        for (var i = 0; i < 256; i++)
        {
            _s[i] = (byte)i;
        }

        var j = (byte)0;
        for (var i = 0; i < 256; i++)
        {
            j = (byte)(j + _s[i] + key[i % key.Length]);
            (_s[i], _s[j]) = (_s[j], _s[i]);
        }

        _i = 0;
        _j = 0;

        if (discard1024)
        {
            Discard(1024);
        }
    }

    public Rc4StreamCipher(byte[] key, bool discard1024 = true)
        : this(key == null ? throw new ArgumentNullException(nameof(key)) : key.AsSpan(), discard1024)
    {
    }

    public void Discard(int count)
    {
        if (count <= 0)
        {
            return;
        }

        var s = _s;
        var i = _i;
        var j = _j;
        var remaining = count;

        while (remaining >= 8)
        {
            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);

            remaining -= 8;
        }

        while (remaining > 0)
        {
            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            (s[i], s[j]) = (s[j], s[i]);
            remaining--;
        }

        _i = i;
        _j = j;
    }

    public void ProcessInPlace(Span<byte> buffer)
    {
        var s = _s;
        var i = _i;
        var j = _j;
        var index = 0;
        var length = buffer.Length;

        while (index <= length - 8)
        {
            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si0 = s[i];
            var sj0 = s[j];
            s[i] = sj0;
            s[j] = si0;
            buffer[index] ^= s[(byte)(si0 + sj0)];

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si1 = s[i];
            var sj1 = s[j];
            s[i] = sj1;
            s[j] = si1;
            buffer[index + 1] ^= s[(byte)(si1 + sj1)];

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si2 = s[i];
            var sj2 = s[j];
            s[i] = sj2;
            s[j] = si2;
            buffer[index + 2] ^= s[(byte)(si2 + sj2)];

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si3 = s[i];
            var sj3 = s[j];
            s[i] = sj3;
            s[j] = si3;
            buffer[index + 3] ^= s[(byte)(si3 + sj3)];

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si4 = s[i];
            var sj4 = s[j];
            s[i] = sj4;
            s[j] = si4;
            buffer[index + 4] ^= s[(byte)(si4 + sj4)];

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si5 = s[i];
            var sj5 = s[j];
            s[i] = sj5;
            s[j] = si5;
            buffer[index + 5] ^= s[(byte)(si5 + sj5)];

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si6 = s[i];
            var sj6 = s[j];
            s[i] = sj6;
            s[j] = si6;
            buffer[index + 6] ^= s[(byte)(si6 + sj6)];

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si7 = s[i];
            var sj7 = s[j];
            s[i] = sj7;
            s[j] = si7;
            buffer[index + 7] ^= s[(byte)(si7 + sj7)];

            index += 8;
        }

        while (index < length)
        {
            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si = s[i];
            var sj = s[j];
            s[i] = sj;
            s[j] = si;
            buffer[index] ^= s[(byte)(si + sj)];
            index++;
        }

        _i = i;
        _j = j;
    }

    public void Process(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("Output span must be at least as long as input span.", nameof(output));
        }

        var s = _s;
        var i = _i;
        var j = _j;
        var index = 0;
        var length = input.Length;

        while (index <= length - 8)
        {
            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si0 = s[i];
            var sj0 = s[j];
            s[i] = sj0;
            s[j] = si0;
            output[index] = (byte)(input[index] ^ s[(byte)(si0 + sj0)]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si1 = s[i];
            var sj1 = s[j];
            s[i] = sj1;
            s[j] = si1;
            output[index + 1] = (byte)(input[index + 1] ^ s[(byte)(si1 + sj1)]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si2 = s[i];
            var sj2 = s[j];
            s[i] = sj2;
            s[j] = si2;
            output[index + 2] = (byte)(input[index + 2] ^ s[(byte)(si2 + sj2)]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si3 = s[i];
            var sj3 = s[j];
            s[i] = sj3;
            s[j] = si3;
            output[index + 3] = (byte)(input[index + 3] ^ s[(byte)(si3 + sj3)]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si4 = s[i];
            var sj4 = s[j];
            s[i] = sj4;
            s[j] = si4;
            output[index + 4] = (byte)(input[index + 4] ^ s[(byte)(si4 + sj4)]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si5 = s[i];
            var sj5 = s[j];
            s[i] = sj5;
            s[j] = si5;
            output[index + 5] = (byte)(input[index + 5] ^ s[(byte)(si5 + sj5)]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si6 = s[i];
            var sj6 = s[j];
            s[i] = sj6;
            s[j] = si6;
            output[index + 6] = (byte)(input[index + 6] ^ s[(byte)(si6 + sj6)]);

            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si7 = s[i];
            var sj7 = s[j];
            s[i] = sj7;
            s[j] = si7;
            output[index + 7] = (byte)(input[index + 7] ^ s[(byte)(si7 + sj7)]);

            index += 8;
        }

        while (index < length)
        {
            i = (byte)(i + 1);
            j = (byte)(j + s[i]);
            var si = s[i];
            var sj = s[j];
            s[i] = sj;
            s[j] = si;
            output[index] = (byte)(input[index] ^ s[(byte)(si + sj)]);
            index++;
        }

        _i = i;
        _j = j;
    }

    public void Process(byte[] input, int inputOffset, int length, byte[] output, int outputOffset)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        Process(input.AsSpan(inputOffset, length), output.AsSpan(outputOffset, length));
    }

    public byte[] Process(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var output = new byte[data.Length];
        Process(data.AsSpan(), output.AsSpan());
        return output;
    }

    public void ProcessInPlace(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ProcessInPlace(data.AsSpan(offset, length));
    }
}
