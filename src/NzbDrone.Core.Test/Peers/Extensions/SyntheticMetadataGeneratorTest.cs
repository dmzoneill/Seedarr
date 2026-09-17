using System.Collections.Generic;
using System.IO;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NUnit.Framework;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class SyntheticMetadataGeneratorTest
{
    private SyntheticMetadataGenerator _generator;

    [SetUp]
    public void Setup()
    {
        _generator = new SyntheticMetadataGenerator();
    }

    [Test]
    public void GenerateMetadataBytes_should_generate_single_file_metadata_with_expected_keys()
    {
        var torrent = new Torrent
        {
            Name = "ubuntu.iso",
            TotalSize = 5 * 1024 * 1024,
            PieceLength = 1024 * 1024,
            InfoHash = "0123456789abcdef0123456789abcdef01234567"
        };

        var bytes = _generator.GenerateMetadataBytes(torrent);
        var dict = ParseBencode(bytes);

        Assert.That(dict, Is.Not.Null);
        Assert.That(((BString)dict["name"]).ToString(), Is.EqualTo("ubuntu.iso"));
        Assert.That((long)((BNumber)dict["length"]).Value, Is.EqualTo(5 * 1024 * 1024));
        Assert.That((long)((BNumber)dict["piece length"]).Value, Is.EqualTo(1024 * 1024));
        Assert.That(dict.ContainsKey("pieces"), Is.True);
        Assert.That(((BString)dict["pieces"]).Value.Length, Is.EqualTo(5 * 20));
        Assert.That(dict.ContainsKey("files"), Is.False);
    }

    [Test]
    public void GenerateMetadataBytes_should_generate_multi_file_metadata_with_files_list()
    {
        var torrent = new Torrent
        {
            Name = "album",
            PieceLength = 512 * 1024,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            Files = new List<TorrentFile>
            {
                new() { Path = "disc1/track1.flac", Size = 20 * 1024 * 1024 },
                new() { Path = "cover.jpg", Size = 500 * 1024 }
            }
        };

        var bytes = _generator.GenerateMetadataBytes(torrent);
        var dict = ParseBencode(bytes);

        Assert.That(dict, Is.Not.Null);
        Assert.That(((BString)dict["name"]).ToString(), Is.EqualTo("album"));
        Assert.That((long)((BNumber)dict["piece length"]).Value, Is.EqualTo(512 * 1024));
        Assert.That(dict.ContainsKey("files"), Is.True);
        Assert.That(dict.ContainsKey("length"), Is.False);

        var filesList = (BList)dict["files"];
        Assert.That(filesList.Count, Is.EqualTo(2));

        var file0 = (BDictionary)filesList[0];
        Assert.That((long)((BNumber)file0["length"]).Value, Is.EqualTo(20 * 1024 * 1024));
        var pathList0 = (BList)file0["path"];
        Assert.That(pathList0.Count, Is.EqualTo(2));
        Assert.That(((BString)pathList0[0]).ToString(), Is.EqualTo("disc1"));
        Assert.That(((BString)pathList0[1]).ToString(), Is.EqualTo("track1.flac"));

        var file1 = (BDictionary)filesList[1];
        Assert.That((long)((BNumber)file1["length"]).Value, Is.EqualTo(500 * 1024));
        var pathList1 = (BList)file1["path"];
        Assert.That(pathList1.Count, Is.EqualTo(1));
        Assert.That(((BString)pathList1[0]).ToString(), Is.EqualTo("cover.jpg"));
    }

    [Test]
    public void GenerateMetadataBytes_should_generate_deterministic_piece_hashes_matching_piece_count()
    {
        var torrent = new Torrent
        {
            Name = "deterministic.dat",
            TotalSize = 3 * 1024 * 1024,
            PieceLength = 1024 * 1024,
            InfoHash = "1122334455667788990011223344556677889900"
        };

        var bytes1 = _generator.GenerateMetadataBytes(torrent);
        var bytes2 = _generator.GenerateMetadataBytes(torrent);

        Assert.That(bytes1, Is.EqualTo(bytes2));

        var dict = ParseBencode(bytes1);
        var pieces = ((BString)dict["pieces"]).Value;

        // 3 MiB with 1 MiB pieces -> exactly 3 pieces -> 60 bytes
        Assert.That(pieces.Length, Is.EqualTo(60));
    }

    [Test]
    public void GenerateMetadataBytes_should_use_default_piece_length_when_unset()
    {
        var torrent = new Torrent
        {
            Name = "zero_piecelen.dat",
            TotalSize = 2 * 1024 * 1024,
            PieceLength = 0,
            InfoHash = "0123456789abcdef0123456789abcdef01234567"
        };

        var bytes = _generator.GenerateMetadataBytes(torrent);
        var dict = ParseBencode(bytes);

        Assert.That((long)((BNumber)dict["piece length"]).Value, Is.EqualTo(1024 * 1024));
    }

    [Test]
    public void GenerateMetadataBytes_should_include_private_flag_when_torrent_is_private()
    {
        var torrent = new Torrent
        {
            Name = "private.iso",
            TotalSize = 1024,
            PieceLength = 1024,
            InfoHash = "0123456789abcdef0123456789abcdef01234567",
            IsPrivate = true
        };

        var bytes = _generator.GenerateMetadataBytes(torrent);
        var dict = ParseBencode(bytes);

        Assert.That(dict.ContainsKey("private"), Is.True);
        Assert.That((int)((BNumber)dict["private"]).Value, Is.EqualTo(1));
    }

    private static BDictionary ParseBencode(byte[] data)
    {
        var parser = new BencodeParser();
        using var stream = new MemoryStream(data);
        return parser.Parse<BDictionary>(stream);
    }
}
