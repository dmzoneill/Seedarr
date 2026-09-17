using System;
using System.Collections.Generic;
using System.Net;

namespace NzbDrone.Core.Peers.Extensions;

public class ExtensionHandshake
{
    public Dictionary<string, int> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> M => Extensions;
    public string Version { get; set; }
    public string V => Version;
    public int? RequestQueueLength { get; set; }
    public int? Reqq => RequestQueueLength;
    public int? Port { get; set; }
    public int? P => Port;
    public long? MetadataSize { get; set; }
    public IPAddress YourIp { get; set; }
}
