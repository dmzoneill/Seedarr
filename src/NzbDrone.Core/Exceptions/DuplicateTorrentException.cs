using System;

namespace NzbDrone.Core.Exceptions;

public class DuplicateTorrentException : Exception
{
    public string InfoHash { get; }

    public DuplicateTorrentException(string infoHash)
        : base($"Torrent with InfoHash '{infoHash}' already exists.")
    {
        InfoHash = infoHash;
    }

    public DuplicateTorrentException(string infoHash, string message)
        : base(message)
    {
        InfoHash = infoHash;
    }

    public DuplicateTorrentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
