using System;
using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Storage;

public class MultiFilePieceStorage : Torrents.MultiFilePieceStorage, IMultiFileStorage, IPieceStorage
{
    public MultiFilePieceStorage(
        string baseDirectory,
        IList<TorrentFile> files,
        int pieceLength,
        long totalSize = 0,
        IFileHandlePool handlePool = null)
        : base(baseDirectory, files, pieceLength, totalSize, handlePool)
    {
    }

    public MultiFilePieceStorage(
        Torrent torrent,
        IList<TorrentFile> files,
        string baseDirectory = null,
        IFileHandlePool handlePool = null)
        : base(torrent, files, baseDirectory, handlePool)
    {
    }

    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3427:Method overloads with default values should not be ambiguous", Justification = "Constructor supports optional dependencies while parameterless exists for backwards compatibility")]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("csharpsquid", "S3427", Justification = "Constructor supports optional dependencies while parameterless exists for backwards compatibility")]
    public MultiFilePieceStorage(
        IPieceBoundaryResolver resolver = null,
        IFileHandlePool handlePool = null)
        : base(resolver, handlePool)
    {
    }

    public MultiFilePieceStorage()
        : base()
    {
    }
}
