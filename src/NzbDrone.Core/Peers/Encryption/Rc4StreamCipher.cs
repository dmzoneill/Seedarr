using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace NzbDrone.Core.Peers.Encryption;

public class Rc4StreamCipher
{
    private readonly RC4Engine _engine;

    public Rc4StreamCipher(byte[] key, bool discard1024 = true)
    {
        _engine = new RC4Engine();
        _engine.Init(true, new KeyParameter(key));

        if (discard1024)
        {
            // MSE/PE spec: discard first 1024 bytes of RC4 output
            var discard = new byte[1024];
            _engine.ProcessBytes(discard, 0, discard.Length, discard, 0);
        }
    }

    public void Process(byte[] input, int inputOffset, int length, byte[] output, int outputOffset)
    {
        _engine.ProcessBytes(input, inputOffset, length, output, outputOffset);
    }

    public byte[] Process(byte[] data)
    {
        var output = new byte[data.Length];
        _engine.ProcessBytes(data, 0, data.Length, output, 0);
        return output;
    }

    public void ProcessInPlace(byte[] data, int offset, int length)
    {
        _engine.ProcessBytes(data, offset, length, data, offset);
    }
}
