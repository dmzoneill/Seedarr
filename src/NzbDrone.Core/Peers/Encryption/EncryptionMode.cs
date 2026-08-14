using System;

namespace NzbDrone.Core.Peers.Encryption;

[Flags]
public enum CryptoMethod : uint
{
    None = 0,
    PlainText = 0x01,
    Rc4 = 0x02
}

public enum EncryptionMode
{
    PreferPlainText,
    PreferEncrypted,
    RequireEncrypted
}
