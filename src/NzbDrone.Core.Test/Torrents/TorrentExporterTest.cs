using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentExporterTest
{
    private ITorrentFileService _torrentFileService;
    private ITrackerEntryService _trackerEntryService;
    private TorrentExporter _subject;
    private BencodeParser _parser;

    [SetUp]
    public void SetUp()
    {
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _subject = new TorrentExporter(_torrentFileService, _trackerEntryService);
        _parser = new BencodeParser();
    }

    [Test]
    public void ExportTorrent_SynthesizesValidBep3_SingleFile()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "sample.iso",
            TotalSize = 1048576,
            PieceLength = 262144,
            PieceCount = 4,
            TrackerUrl = "http://tracker.example.com/announce",
            Comment = "BEP 3 test",
            CreatedBy = "SeedarrExportTest",
            CreationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsPrivate = false
        };

        var bytes = _subject.ExportTorrent(torrent);

        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes.Length, Is.GreaterThan(0));

        var root = _parser.Parse<BDictionary>(bytes);

        // Top-level keys
        Assert.That(root.ContainsKey("announce"), Is.True);
        Assert.That(root["announce"].ToString(), Is.EqualTo("http://tracker.example.com/announce"));

        Assert.That(root.ContainsKey("announce-list"), Is.True);
        var announceList = (BList)root["announce-list"];
        Assert.That(announceList.Count, Is.EqualTo(1));
        var tier0 = (BList)announceList[0];
        Assert.That(tier0[0].ToString(), Is.EqualTo("http://tracker.example.com/announce"));

        Assert.That(root.ContainsKey("comment"), Is.True);
        Assert.That(root["comment"].ToString(), Is.EqualTo("BEP 3 test"));

        Assert.That(root.ContainsKey("created by"), Is.True);
        Assert.That(root["created by"].ToString(), Is.EqualTo("SeedarrExportTest"));

        Assert.That(root.ContainsKey("creation date"), Is.True);
        var expectedUnix = new DateTimeOffset(torrent.CreationDate.Value).ToUnixTimeSeconds();
        Assert.That(((BNumber)root["creation date"]).Value, Is.EqualTo(expectedUnix));

        // Info dictionary
        Assert.That(root.ContainsKey("info"), Is.True);
        var info = (BDictionary)root["info"];

        Assert.That(info["name"].ToString(), Is.EqualTo("sample.iso"));
        Assert.That(((BNumber)info["piece length"]).Value, Is.EqualTo(262144));
        Assert.That(((BNumber)info["length"]).Value, Is.EqualTo(1048576));
        Assert.That(info.ContainsKey("files"), Is.False);
        Assert.That(info.ContainsKey("private"), Is.False);

        var pieces = ((BString)info["pieces"]).Value;
        Assert.That(pieces.Length, Is.EqualTo(4 * 20));
    }

    [Test]
    public void ExportTorrent_SynthesizesValidBep3_MultiFile()
    {
        var torrent = new Torrent
        {
            Id = 2,
            Name = "CoolAlbum",
            TotalSize = 110000,
            PieceLength = 65536,
            PieceCount = 2,
            TrackerUrl = "http://tracker1.org/announce",
            IsPrivate = true,
            Files = new List<TorrentFile>
            {
                new() { Path = "CoolAlbum/track01.flac", Size = 50000 },
                new() { Path = "CoolAlbum/disc2/track02.flac", Size = 60000 }
            }
        };

        var trackers = new List<TrackerEntry>
        {
            new() { TorrentId = 2, Url = "http://tier0.org/announce", Tier = 0 },
            new() { TorrentId = 2, Url = "http://tier1.org/announce", Tier = 1 }
        };

        var bytes = _subject.ExportTorrent(torrent, torrent.Files, trackers);

        Assert.That(bytes, Is.Not.Null);
        var root = _parser.Parse<BDictionary>(bytes);

        // Top-level announce list
        var announceList = (BList)root["announce-list"];
        Assert.That(announceList.Count, Is.EqualTo(2));

        // Info dictionary
        var info = (BDictionary)root["info"];
        Assert.That(info["name"].ToString(), Is.EqualTo("CoolAlbum"));
        Assert.That(info.ContainsKey("length"), Is.False);
        Assert.That(info.ContainsKey("files"), Is.True);
        Assert.That(((BNumber)info["private"]).Value, Is.EqualTo(1));

        var files = (BList)info["files"];
        Assert.That(files.Count, Is.EqualTo(2));

        var f1 = (BDictionary)files[0];
        Assert.That(((BNumber)f1["length"]).Value, Is.EqualTo(50000));
        var path1 = (BList)f1["path"];
        Assert.That(path1.Count, Is.EqualTo(1));
        Assert.That(path1[0].ToString(), Is.EqualTo("track01.flac"));

        var f2 = (BDictionary)files[1];
        Assert.That(((BNumber)f2["length"]).Value, Is.EqualTo(60000));
        var path2 = (BList)f2["path"];
        Assert.That(path2.Count, Is.EqualTo(2));
        Assert.That(path2[0].ToString(), Is.EqualTo("disc2"));
        Assert.That(path2[1].ToString(), Is.EqualTo("track02.flac"));
    }

    [Test]
    public void ExportTorrent_ExistingSourcePath_ReturnsFileBytes()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var testDict = new BDictionary
            {
                ["announce"] = new BString("http://original.tracker/announce"),
                ["info"] = new BDictionary
                {
                    ["name"] = new BString("original"),
                    ["piece length"] = new BNumber(16384),
                    ["pieces"] = new BString(new byte[20]),
                    ["length"] = new BNumber(100)
                }
            };
            var expectedBytes = testDict.EncodeAsBytes();
            File.WriteAllBytes(tempFile, expectedBytes);

            var torrent = new Torrent
            {
                Id = 3,
                Name = "SomethingElse",
                SourcePath = tempFile
            };

            var bytes = _subject.ExportTorrent(torrent);
            Assert.That(bytes, Is.EqualTo(expectedBytes));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void ExportTorrent_CustomPieceHashes_UsesProvidedPieceHashes()
    {
        var pieceHashes = new byte[40];
        Array.Fill(pieceHashes, (byte)0x77);

        var torrent = new Torrent
        {
            Id = 4,
            Name = "custom_pieces.dat",
            TotalSize = 32768,
            PieceLength = 16384,
            PieceCount = 2,
            PieceHashes = pieceHashes
        };

        var bytes = _subject.ExportTorrent(torrent);
        var root = _parser.Parse<BDictionary>(bytes);
        var info = (BDictionary)root["info"];
        var pieces = ((BString)info["pieces"]).Value;

        Assert.That(pieces, Is.EqualTo(pieceHashes));
    }
}
