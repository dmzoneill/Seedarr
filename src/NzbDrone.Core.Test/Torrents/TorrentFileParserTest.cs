using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentFileParserTest
{
    private TorrentFileParser _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new TorrentFileParser();
    }

    private static Stream CreateTorrentStream(BDictionary torrentDict)
    {
        var bytes = torrentDict.EncodeAsBytes();
        return new MemoryStream(bytes);
    }

    private static BDictionary CreateMinimalTorrent(string name = "test-file.txt", long fileSize = 1024, int pieceLength = 16384)
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var info = new BDictionary
        {
            { "name", new BString(name) },
            { "piece length", new BNumber(pieceLength) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(fileSize) }
        };

        return new BDictionary
        {
            { "info", info }
        };
    }

    [Test]
    public void Parse_should_extract_name_from_single_file_torrent()
    {
        var torrentDict = CreateMinimalTorrent("my-file.iso");
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("my-file.iso"));
    }

    [Test]
    public void Parse_should_extract_total_size_from_single_file_torrent()
    {
        var torrentDict = CreateMinimalTorrent(fileSize: 5000);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.TotalSize, Is.EqualTo(5000));
    }

    [Test]
    public void Parse_should_extract_piece_length()
    {
        var torrentDict = CreateMinimalTorrent(pieceLength: 262144);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.PieceLength, Is.EqualTo(262144));
    }

    [Test]
    public void Parse_should_calculate_piece_count()
    {
        var pieces = new byte[40];
        new Random(42).NextBytes(pieces);

        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.PieceCount, Is.EqualTo(2));
    }

    [Test]
    public void Parse_should_calculate_info_hash()
    {
        var torrentDict = CreateMinimalTorrent();
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.InfoHash, Is.Not.Null);
        Assert.That(result.InfoHash.Length, Is.EqualTo(40));
    }

    [Test]
    public void Parse_should_extract_comment_when_present()
    {
        var torrentDict = CreateMinimalTorrent();
        torrentDict.Add("comment", new BString("Test comment"));
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Comment, Is.EqualTo("Test comment"));
    }

    [Test]
    public void Parse_should_set_comment_to_null_when_not_present()
    {
        var torrentDict = CreateMinimalTorrent();
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Comment, Is.Null);
    }

    [Test]
    public void Parse_should_extract_created_by_when_present()
    {
        var torrentDict = CreateMinimalTorrent();
        torrentDict.Add("created by", new BString("MyClient/1.0"));
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.CreatedBy, Is.EqualTo("MyClient/1.0"));
    }

    [Test]
    public void Parse_should_set_created_by_to_null_when_not_present()
    {
        var torrentDict = CreateMinimalTorrent();
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.CreatedBy, Is.Null);
    }

    [Test]
    public void Parse_should_extract_creation_date_when_present()
    {
        var torrentDict = CreateMinimalTorrent();
        var expectedDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var unixTime = new DateTimeOffset(expectedDate).ToUnixTimeSeconds();
        torrentDict.Add("creation date", new BNumber(unixTime));
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.CreationDate, Is.EqualTo(expectedDate));
    }

    [Test]
    public void Parse_should_set_creation_date_to_null_when_not_present()
    {
        var torrentDict = CreateMinimalTorrent();
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.CreationDate, Is.Null);
    }

    [Test]
    public void Parse_should_detect_private_flag()
    {
        var torrentDict = CreateMinimalTorrent();
        var info = (BDictionary)torrentDict["info"];
        info.Add("private", new BNumber(1));
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.IsPrivate, Is.True);
    }

    [Test]
    public void Parse_should_set_private_false_when_flag_not_present()
    {
        var torrentDict = CreateMinimalTorrent();
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.IsPrivate, Is.False);
    }

    [Test]
    public void Parse_should_set_private_false_when_flag_is_zero()
    {
        var torrentDict = CreateMinimalTorrent();
        var info = (BDictionary)torrentDict["info"];
        info.Add("private", new BNumber(0));
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.IsPrivate, Is.False);
    }

    [Test]
    public void Parse_should_extract_announce_url_when_present()
    {
        var torrentDict = CreateMinimalTorrent();
        torrentDict.Add("announce", new BString("http://tracker.example.com/announce"));
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.AnnounceUrl, Is.EqualTo("http://tracker.example.com/announce"));
    }

    [Test]
    public void Parse_should_set_announce_url_to_null_when_not_present()
    {
        var torrentDict = CreateMinimalTorrent();
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.AnnounceUrl, Is.Null);
    }

    [Test]
    public void Parse_should_extract_announce_list_when_present()
    {
        var torrentDict = CreateMinimalTorrent();
        var announceList = new BList
        {
            new BList { new BString("http://tracker1.example.com/announce"), new BString("http://tracker2.example.com/announce") },
            new BList { new BString("http://tracker3.example.com/announce") }
        };
        torrentDict.Add("announce-list", announceList);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.AnnounceList, Has.Count.EqualTo(2));
        Assert.That(result.AnnounceList[0], Has.Count.EqualTo(2));
        Assert.That(result.AnnounceList[1], Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_should_handle_multi_file_torrent()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1000) },
                { "path", new BList { new BString("folder"), new BString("file1.txt") } }
            },
            new BDictionary
            {
                { "length", new BNumber(2000) },
                { "path", new BList { new BString("folder"), new BString("file2.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "files", files }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files[0].Path, Is.EqualTo("my-torrent/folder/file1.txt"));
        Assert.That(result.Files[0].Size, Is.EqualTo(1000));
        Assert.That(result.Files[1].Path, Is.EqualTo("my-torrent/folder/file2.txt"));
        Assert.That(result.Files[1].Size, Is.EqualTo(2000));
    }

    [Test]
    public void Parse_should_prefix_multi_file_paths_with_root_dir_and_normalize_separators()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1234) },
                { "path", new BList { new BString("/CD1/"), new BString("01.flac") } }
            },
            new BDictionary
            {
                { "length", new BNumber(5678) },
                { "path", new BList { new BString(@"CD2\subfolder"), new BString("02.flac") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString(@"\AlbumXYZ/") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "files", files }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files[0].Path, Is.EqualTo("AlbumXYZ/CD1/01.flac"));
        Assert.That(result.Files[0].Size, Is.EqualTo(1234));
        Assert.That(result.Files[1].Path, Is.EqualTo("AlbumXYZ/CD2/subfolder/02.flac"));
        Assert.That(result.Files[1].Size, Is.EqualTo(5678));
    }

    [Test]
    public void Parse_should_sum_total_size_for_multi_file_torrent()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1000) },
                { "path", new BList { new BString("file1.txt") } }
            },
            new BDictionary
            {
                { "length", new BNumber(2000) },
                { "path", new BList { new BString("file2.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("multi") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "files", files }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.TotalSize, Is.EqualTo(3000));
    }

    [Test]
    public void Parse_should_create_single_file_entry_for_single_file_torrent()
    {
        var torrentDict = CreateMinimalTorrent("single.iso", 4096);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("single.iso"));
        Assert.That(result.Files[0].Size, Is.EqualTo(4096));
    }

    [Test]
    public void Parse_should_produce_consistent_info_hash()
    {
        var torrentDict = CreateMinimalTorrent();
        var bytes = torrentDict.EncodeAsBytes();

        var result1 = _subject.Parse(new MemoryStream(bytes));
        var result2 = _subject.Parse(new MemoryStream(bytes));

        Assert.That(result1.InfoHash, Is.EqualTo(result2.InfoHash));
    }

    [Test]
    public void Parse_should_set_announce_list_to_null_when_not_present()
    {
        var torrentDict = CreateMinimalTorrent();
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.AnnounceList, Is.Null);
    }

    [Test]
    public void Parse_should_extract_flat_announce_list_when_present()
    {
        var torrentDict = CreateMinimalTorrent();
        var announceList = new BList
        {
            new BString("udp://tracker1.example.com:6969/announce"),
            new BString("udp://tracker2.example.com:6969/announce")
        };
        torrentDict.Add("announce-list", announceList);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.AnnounceList, Has.Count.EqualTo(2));
        Assert.That(result.AnnounceList[0][0], Is.EqualTo("udp://tracker1.example.com:6969/announce"));
        Assert.That(result.AnnounceList[1][0], Is.EqualTo("udp://tracker2.example.com:6969/announce"));
    }

    [Test]
    public void Parse_should_fallback_announce_url_to_first_announce_list_entry_when_announce_not_set()
    {
        var torrentDict = CreateMinimalTorrent();
        var announceList = new BList
        {
            new BList { new BString("udp://tracker-primary.example.com:1337/announce") }
        };
        torrentDict.Add("announce-list", announceList);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.AnnounceUrl, Is.EqualTo("udp://tracker-primary.example.com:1337/announce"));
    }

    [Test]
    public void Parse_should_throw_when_stream_exceeds_10_mib()
    {
        var mockStream = Substitute.For<Stream>();
        mockStream.CanSeek.Returns(true);
        mockStream.Length.Returns((10 * 1024 * 1024) + 1);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(mockStream));
        Assert.That(ex.Message, Does.Contain("10 MiB"));
    }

    [TestCase(1337)]
    [TestCase(50000)]
    [TestCase(512)]
    [TestCase(8192)]
    [TestCase(0)]
    [TestCase(-16384)]
    [TestCase(134217728)] // > 64 MiB
    public void Parse_should_throw_when_piece_length_is_not_valid_power_of_two(int invalidPieceLength)
    {
        var torrentDict = CreateMinimalTorrent(pieceLength: invalidPieceLength);
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Invalid piece length"));
    }

    [Test]
    public void Parse_should_throw_when_piece_count_exceeds_500000()
    {
        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[500001 * 20]) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Piece count 500001 exceeds maximum permitted limit"));
    }

    [Test]
    public void Parse_should_throw_when_piece_count_is_zero()
    {
        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(Array.Empty<byte>()) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Piece count 0 exceeds maximum permitted limit"));
    }

    [Test]
    public void Parse_should_throw_when_single_file_length_is_negative()
    {
        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "length", new BNumber(-100) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("negative file length"));
    }

    [Test]
    public void Parse_should_throw_when_multi_file_length_is_negative()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(-500) },
                { "path", new BList { new BString("file.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("negative file length"));
    }

    [Test]
    public void Parse_should_throw_when_recursion_depth_exceeds_limit()
    {
        var current = new BDictionary();
        var root = current;
        for (var i = 0; i < 35; i++)
        {
            var next = new BDictionary();
            current.Add($"key{i}", next);
            current = next;
        }

        using var stream = CreateTorrentStream(root);
        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("recursion depth"));
    }

    [Test]
    public void Parse_should_succeed_for_valid_torrent()
    {
        var torrentDict = CreateMinimalTorrent("valid-torrent.bin", 65536, 16384);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("valid-torrent.bin"));
        Assert.That(result.TotalSize, Is.EqualTo(65536));
        Assert.That(result.PieceLength, Is.EqualTo(16384));
        Assert.That(result.PieceCount, Is.EqualTo(1));
    }

    [Test]
    public void Parse_should_prioritize_name_utf8_over_legacy_name()
    {
        var info = new BDictionary
        {
            { "name", new BString("legacy-name") },
            { "name.utf-8", new BString("進撃の巨人") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("進撃の巨人"));
    }

    [Test]
    public void Parse_should_prioritize_name_utf8_without_hyphen_over_legacy_name()
    {
        var info = new BDictionary
        {
            { "name", new BString("legacy-name") },
            { "name.utf8", new BString("進撃の巨人") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("進撃の巨人"));
    }

    [Test]
    public void Parse_should_prioritize_name_dot_utf8_with_hyphen_over_without_hyphen()
    {
        var info = new BDictionary
        {
            { "name", new BString("legacy-name") },
            { "name.utf8", new BString("utf8-name") },
            { "name.utf-8", new BString("utf-8-name") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("utf-8-name"));
    }

    [Test]
    public void Parse_should_prioritize_path_utf8_over_legacy_path_in_multifile_torrent()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) },
                { "path", new BList { new BString("ascii_dir"), new BString("ascii_file.txt") } },
                { "path.utf-8", new BList { new BString("日本語フォルダ"), new BString("ファイル.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("my-torrent/日本語フォルダ/ファイル.txt"));
    }

    [Test]
    public void Parse_should_prioritize_path_utf8_without_hyphen_over_legacy_path()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) },
                { "path", new BList { new BString("legacy"), new BString("file.txt") } },
                { "path.utf8", new BList { new BString("utf8_dir"), new BString("file.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("my-torrent/utf8_dir/file.txt"));
    }

    [Test]
    public void Parse_should_fallback_to_standard_name_and_path_when_utf8_keys_absent()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(2048) },
                { "path", new BList { new BString("standard_folder"), new BString("standard_file.mkv") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("standard-torrent-name") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("standard-torrent-name"));
        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("standard-torrent-name/standard_folder/standard_file.mkv"));
    }

    [Test]
    public void Parse_should_gracefully_decode_non_utf8_bytes_in_legacy_name()
    {
        // 0xE9, 0x6C, 0xE8, 0x76, 0x65 is ISO-8859-1 for "élève" (invalid UTF-8 sequence)
        var nonUtf8Bytes = new byte[] { 0xE9, 0x6C, 0xE8, 0x76, 0x65 };
        var info = new BDictionary
        {
            { "name", new BString(nonUtf8Bytes) },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("élève"));
    }

    [Test]
    public void Parse_should_gracefully_decode_non_utf8_bytes_in_legacy_path()
    {
        var nonUtf8DirBytes = new byte[] { 0xE9, 0x74, 0xE9 }; // "été" in ISO-8859-1
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) },
                { "path", new BList { new BString(nonUtf8DirBytes), new BString("file.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("my-torrent/été/file.txt"));
    }

    [Test]
    public void Parse_should_throw_when_name_and_utf8_variants_missing()
    {
        var info = new BDictionary
        {
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "length", new BNumber(1024) }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("missing or invalid 'name'"));
    }

    [Test]
    public void Parse_should_throw_when_path_and_utf8_variants_missing()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("missing or invalid 'path'"));
    }

    [Test]
    public void Parse_should_flag_file_with_attr_p_as_padding_file()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) },
                { "path", new BList { new BString("video.mkv") } }
            },
            new BDictionary
            {
                { "length", new BNumber(512) },
                { "path", new BList { new BString("pad_file.dat") } },
                { "attr", new BString("p") }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files[0].IsPaddingFile, Is.False);
        Assert.That(result.Files[1].IsPaddingFile, Is.True);
    }

    [Test]
    public void Parse_should_flag_file_with_padding_filename_convention_as_padding_file()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) },
                { "path", new BList { new BString("video.mkv") } }
            },
            new BDictionary
            {
                { "length", new BNumber(256) },
                { "path", new BList { new BString("_____padding_file_0_____") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files[0].IsPaddingFile, Is.False);
        Assert.That(result.Files[1].IsPaddingFile, Is.True);
    }

    [Test]
    public void Parse_should_flag_file_with_dot_pad_path_as_padding_file()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) },
                { "path", new BList { new BString("video.mkv") } }
            },
            new BDictionary
            {
                { "length", new BNumber(512) },
                { "path", new BList { new BString(".pad"), new BString("123") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files[0].IsPaddingFile, Is.False);
        Assert.That(result.Files[1].IsPaddingFile, Is.True);
    }

    [Test]
    public void Parse_should_flag_standard_payload_files_as_not_padding_file()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1024) },
                { "path", new BList { new BString("dir"), new BString("video.mkv") } }
            },
            new BDictionary
            {
                { "length", new BNumber(512) },
                { "path", new BList { new BString("dir"), new BString("sample.nfo") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files[0].IsPaddingFile, Is.False);
        Assert.That(result.Files[1].IsPaddingFile, Is.False);
    }

    [Test]
    public void Parse_should_compute_content_size_excluding_padding_files()
    {
        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(2000) },
                { "path", new BList { new BString("movie.mkv") } }
            },
            new BDictionary
            {
                { "length", new BNumber(1000) },
                { "path", new BList { new BString("_____padding_file_1_____") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("my-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(new byte[20]) },
            { "files", files }
        };
        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.TotalSize, Is.EqualTo(3000));
        Assert.That(result.ContentSize, Is.EqualTo(2000));
    }

    [Test]
    public void Parse_should_compute_info_hash_from_raw_bytes_preserving_non_canonical_key_ordering()
    {
        // Non-canonical key ordering inside info dict: "name" comes before "length" (canonical requires "length" < "name")
        var nonCanonicalInfo = "d4:name8:test.txt6:lengthi1024e12:piece lengthi16384e6:pieces20:12345678901234567890e"u8.ToArray();
        var torrentBytes = "d4:info"u8.ToArray()
            .Concat(nonCanonicalInfo)
            .Concat("e"u8.ToArray())
            .ToArray();

        var expectedHash = Convert.ToHexString(SHA1.HashData(nonCanonicalInfo)).ToLowerInvariant();

        var resultStream = _subject.Parse(new MemoryStream(torrentBytes));
        var resultBytes = _subject.Parse(torrentBytes);

        Assert.That(resultStream.InfoHash, Is.EqualTo(expectedHash));
        Assert.That(resultBytes.InfoHash, Is.EqualTo(expectedHash));

        // Re-encoding through BDictionary sorts keys canonically, which produces a different hash
        var parser = new BencodeParser();
        var parsedDict = parser.Parse<BDictionary>(torrentBytes);
        var canonicalReencodedBytes = ((BDictionary)parsedDict["info"]).EncodeAsBytes();
        var canonicalHash = Convert.ToHexString(SHA1.HashData(canonicalReencodedBytes)).ToLowerInvariant();

        Assert.That(resultStream.InfoHash, Is.Not.EqualTo(canonicalHash));
    }

    [Test]
    public void Parse_should_compute_info_hash_from_raw_bytes_with_non_standard_encodings()
    {
        // Non-standard binary data and custom keys inside info dictionary
        var prefix = "d4:name8:test.txt6:custom5:"u8.ToArray();
        var customBinary = new byte[] { 0x00, 0xFF, 0xFE, 0x01, 0x7F };
        var suffix = "6:lengthi2048e12:piece lengthi16384e6:pieces20:12345678901234567890e"u8.ToArray();

        var nonStandardInfo = prefix.Concat(customBinary).Concat(suffix).ToArray();
        var torrentBytes = "d4:info"u8.ToArray()
            .Concat(nonStandardInfo)
            .Concat("e"u8.ToArray())
            .ToArray();

        var expectedHash = Convert.ToHexString(SHA1.HashData(nonStandardInfo)).ToLowerInvariant();

        var result = _subject.Parse(torrentBytes);

        Assert.That(result.InfoHash, Is.EqualTo(expectedHash));
    }

    [Test]
    public void Parse_byte_array_should_throw_when_null()
    {
        Assert.Throws<ArgumentNullException>(() => _subject.Parse((byte[])null));
    }

    [Test]
    public void Parse_byte_array_should_throw_when_exceeds_10_mib()
    {
        var oversized = new byte[(10 * 1024 * 1024) + 1];
        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(oversized));
        Assert.That(ex.Message, Does.Contain("10 MiB"));
    }

    [Test]
    public void TryExtractRawInfoBytes_should_return_false_when_root_is_not_dictionary()
    {
        var bytes = "i12345e"u8.ToArray();
        var success = TorrentFileParser.TryExtractRawInfoBytes(bytes, out _);
        Assert.That(success, Is.False);
    }

    [Test]
    public void TryExtractRawInfoBytes_should_return_false_when_info_key_is_missing()
    {
        var bytes = "d4:name8:test.txte"u8.ToArray();
        var success = TorrentFileParser.TryExtractRawInfoBytes(bytes, out _);
        Assert.That(success, Is.False);
    }

    [Test]
    public void TryExtractRawInfoBytes_should_return_false_when_info_value_is_not_dictionary()
    {
        var bytes = "d4:info5:helloe"u8.ToArray();
        var success = TorrentFileParser.TryExtractRawInfoBytes(bytes, out _);
        Assert.That(success, Is.False);
    }

    [Test]
    public void TryExtractRawInfoBytes_should_return_false_when_info_dictionary_is_truncated()
    {
        var bytes = "d4:infod4:name5:hello"u8.ToArray();
        var success = TorrentFileParser.TryExtractRawInfoBytes(bytes, out _);
        Assert.That(success, Is.False);
    }

    [Test]
    public void Parse_should_support_bep0047_name_utf8_in_info_dictionary()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var info = new BDictionary
        {
            { "name", new BString("ascii-fallback.txt") },
            { "name.utf-8", new BString("Ren\u00e9_L\u00e9vesque.txt") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(1024) }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("Ren\u00e9_L\u00e9vesque.txt"));
    }

    [Test]
    public void Parse_should_support_bep0047_path_utf8_in_files_dictionary()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var fileDict = new BDictionary
        {
            { "length", new BNumber(2048) },
            { "path", new BList { new BString("CD1"), new BString("track01.mp3") } },
            { "path.utf-8", new BList { new BString("Disc 1"), new BString("01 - Ch\u00e2teau.flac") } }
        };

        var info = new BDictionary
        {
            { "name", new BString("MyAlbum") },
            { "name.utf-8", new BString("My_Ch\u00e2teau_Album") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "files", new BList { fileDict } }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo("My_Ch\u00e2teau_Album"));
        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("My_Ch\u00e2teau_Album/Disc 1/01 - Ch\u00e2teau.flac"));
    }

    [Test]
    public void Parse_should_normalize_names_and_paths_to_unicode_nfc()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        // NFD decomposed form: "e\u0301" (e + combining acute)
        // NFC composed form: "\u00e9"
        var decomposedName = "Re\u0301sume\u0301";
        var composedName = "R\u00e9sum\u00e9";

        var fileDict = new BDictionary
        {
            { "length", new BNumber(1024) },
            { "path", new BList { new BString("Disc 1"), new BString(decomposedName + ".mp3") } }
        };

        var info = new BDictionary
        {
            { "name", new BString(decomposedName) },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "files", new BList { fileDict } }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Name, Is.EqualTo(composedName));
        Assert.That(result.Files[0].Path, Is.EqualTo($"{composedName}/Disc 1/{composedName}.mp3"));
        Assert.That(result.Name.IsNormalized(System.Text.NormalizationForm.FormC), Is.True);
        Assert.That(result.Files[0].Path.IsNormalized(System.Text.NormalizationForm.FormC), Is.True);
    }

    [Test]
    public void Parse_should_extract_single_httpseeds_from_root()
    {
        var torrentDict = CreateMinimalTorrent("test.iso");
        torrentDict["httpseeds"] = new BString("http://seed.example.com/endpoint");

        using var stream = CreateTorrentStream(torrentDict);
        var result = _subject.Parse(stream);

        Assert.That(result.HttpSeeds, Is.Not.Null);
        Assert.That(result.HttpSeeds, Has.Count.EqualTo(1));
        Assert.That(result.HttpSeeds[0], Is.EqualTo("http://seed.example.com/endpoint"));
    }

    [Test]
    public void Parse_should_extract_list_httpseeds_from_root_and_info()
    {
        var torrentDict = CreateMinimalTorrent("test.iso");
        torrentDict["httpseeds"] = new BList
        {
            new BString("http://seed1.example.com/endpoint"),
            new BString("http://seed2.example.com/endpoint")
        };

        var info = torrentDict["info"] as BDictionary;
        info["httpseeds"] = new BList
        {
            new BString("http://seed3.example.com/endpoint"),
            new BString("http://seed1.example.com/endpoint") // Duplicate, should be deduplicated
        };

        using var stream = CreateTorrentStream(torrentDict);
        var result = _subject.Parse(stream);

        Assert.That(result.HttpSeeds, Is.Not.Null);
        Assert.That(result.HttpSeeds, Has.Count.EqualTo(3));
        Assert.That(result.HttpSeeds, Does.Contain("http://seed1.example.com/endpoint"));
        Assert.That(result.HttpSeeds, Does.Contain("http://seed2.example.com/endpoint"));
        Assert.That(result.HttpSeeds, Does.Contain("http://seed3.example.com/endpoint"));
    }

    [Test]
    public void Parse_should_extract_url_list_from_root_and_info()
    {
        var torrentDict = CreateMinimalTorrent("test.iso");
        torrentDict["url-list"] = new BString("http://webseed1.example.com/files/");

        var info = torrentDict["info"] as BDictionary;
        info["url-list"] = new BList
        {
            new BString("http://webseed2.example.com/files/")
        };

        using var stream = CreateTorrentStream(torrentDict);
        var result = _subject.Parse(stream);

        Assert.That(result.UrlList, Is.Not.Null);
        Assert.That(result.UrlList, Has.Count.EqualTo(2));
        Assert.That(result.UrlList[0], Is.EqualTo("http://webseed1.example.com/files/"));
        Assert.That(result.UrlList[1], Is.EqualTo("http://webseed2.example.com/files/"));
        Assert.That(result.WebSeeds, Is.EqualTo(result.UrlList));
    }

    [Test]
    public void Parse_should_identify_pure_v1_torrent_and_set_meta_version_1()
    {
        var torrentDict = CreateMinimalTorrent("file.iso", 2048, 16384);
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.MetaVersion, Is.EqualTo(1));
        Assert.That(result.InfoHash, Is.Not.Null);
        Assert.That(result.InfoHash, Has.Length.EqualTo(40));
        Assert.That(result.InfoHashV2, Is.Null);
        Assert.That(result.PieceLayers, Is.Empty);
        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].PiecesRoot, Is.Null);
        Assert.That(result.Files[0].PiecesRootHex, Is.Null);
    }

    [Test]
    public void Parse_should_parse_pure_v2_torrent_with_nested_directories_in_file_tree()
    {
        var rootA = new byte[32];
        var rootB = new byte[32];
        new Random(101).NextBytes(rootA);
        new Random(202).NextBytes(rootB);

        var fileTree = new BDictionary
        {
            ["docs"] = new BDictionary
            {
                ["sub"] = new BDictionary
                {
                    ["readme.txt"] = new BDictionary
                    {
                        [""] = new BDictionary
                        {
                            ["length"] = new BNumber(25000),
                            ["pieces root"] = new BString(rootA)
                        }
                    }
                },
                ["guide.pdf"] = new BDictionary
                {
                    [""] = new BDictionary
                    {
                        ["length"] = new BNumber(35000),
                        ["pieces root"] = new BString(rootB)
                    }
                }
            }
        };

        var info = new BDictionary
        {
            ["name"] = new BString("docs-bundle"),
            ["piece length"] = new BNumber(16384),
            ["meta version"] = new BNumber(2),
            ["file tree"] = fileTree
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.MetaVersion, Is.EqualTo(2));
        Assert.That(result.InfoHash, Is.Null);
        Assert.That(result.InfoHashV2, Is.Not.Null);
        Assert.That(result.InfoHashV2, Has.Length.EqualTo(64));
        Assert.That(result.Name, Is.EqualTo("docs-bundle"));
        Assert.That(result.PieceLength, Is.EqualTo(16384));
        Assert.That(result.PieceCount, Is.EqualTo(5));
        Assert.That(result.TotalSize, Is.EqualTo(60000));
        Assert.That(result.ContentSize, Is.EqualTo(60000));

        Assert.That(result.Files, Has.Count.EqualTo(2));

        var file1 = result.Files.FirstOrDefault(f => f.Path == "docs/sub/readme.txt");
        Assert.That(file1, Is.Not.Null);
        Assert.That(file1.Size, Is.EqualTo(25000));
        Assert.That(file1.PiecesRoot, Is.EqualTo(rootA));
        Assert.That(file1.PiecesRootHex, Is.EqualTo(Convert.ToHexString(rootA).ToLowerInvariant()));

        var file2 = result.Files.FirstOrDefault(f => f.Path == "docs/guide.pdf");
        Assert.That(file2, Is.Not.Null);
        Assert.That(file2.Size, Is.EqualTo(35000));
        Assert.That(file2.PiecesRoot, Is.EqualTo(rootB));
        Assert.That(file2.PiecesRootHex, Is.EqualTo(Convert.ToHexString(rootB).ToLowerInvariant()));
    }

    [Test]
    public void Parse_should_parse_hybrid_torrent_and_calculate_both_info_hashes()
    {
        var pieces = new byte[40];
        new Random(42).NextBytes(pieces);

        var rootHash = new byte[32];
        new Random(43).NextBytes(rootHash);

        var fileTree = new BDictionary
        {
            ["video.mp4"] = new BDictionary
            {
                [""] = new BDictionary
                {
                    ["length"] = new BNumber(32000),
                    ["pieces root"] = new BString(rootHash)
                }
            }
        };

        var files = new BList
        {
            new BDictionary
            {
                ["length"] = new BNumber(32000),
                ["path"] = new BList { new BString("video.mp4") }
            }
        };

        var info = new BDictionary
        {
            ["name"] = new BString("hybrid-collection"),
            ["piece length"] = new BNumber(16384),
            ["pieces"] = new BString(pieces),
            ["meta version"] = new BNumber(2),
            ["file tree"] = fileTree,
            ["files"] = files
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.MetaVersion, Is.EqualTo(3));
        Assert.That(result.InfoHash, Is.Not.Null);
        Assert.That(result.InfoHash, Has.Length.EqualTo(40));
        Assert.That(result.InfoHashV2, Is.Not.Null);
        Assert.That(result.InfoHashV2, Has.Length.EqualTo(64));

        var encodedInfo = info.EncodeAsBytes();
        var expectedV1 = Convert.ToHexString(SHA1.HashData(encodedInfo)).ToLowerInvariant();
        var expectedV2 = Convert.ToHexString(SHA256.HashData(encodedInfo)).ToLowerInvariant();

        Assert.That(result.InfoHash, Is.EqualTo(expectedV1));
        Assert.That(result.InfoHashV2, Is.EqualTo(expectedV2));
        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("video.mp4"));
        Assert.That(result.Files[0].PiecesRoot, Is.EqualTo(rootHash));
        Assert.That(result.Files[0].PiecesRootHex, Is.EqualTo(Convert.ToHexString(rootHash).ToLowerInvariant()));
    }

    [Test]
    public void Parse_should_extract_piece_layers_dictionary()
    {
        var rootHash = new byte[32];
        new Random(50).NextBytes(rootHash);

        var pieceHashes = new byte[64];
        new Random(51).NextBytes(pieceHashes);

        var fileTree = new BDictionary
        {
            ["largefile.dat"] = new BDictionary
            {
                [""] = new BDictionary
                {
                    ["length"] = new BNumber(32768),
                    ["pieces root"] = new BString(rootHash)
                }
            }
        };

        var info = new BDictionary
        {
            ["name"] = new BString("v2-with-layers"),
            ["piece length"] = new BNumber(16384),
            ["meta version"] = new BNumber(2),
            ["file tree"] = fileTree
        };

        var pieceLayers = new BDictionary
        {
            [new BString(rootHash)] = new BString(pieceHashes)
        };

        var torrentDict = new BDictionary
        {
            ["info"] = info,
            ["piece layers"] = pieceLayers
        };

        using var stream = CreateTorrentStream(torrentDict);
        var result = _subject.Parse(stream);

        var rootHex = Convert.ToHexString(rootHash).ToLowerInvariant();
        Assert.That(result.PieceLayers, Contains.Key(rootHex));
        Assert.That(result.PieceLayers[rootHex], Is.EqualTo(pieceHashes));
    }

    [Test]
    public void Parse_should_throw_when_missing_both_pieces_and_file_tree()
    {
        var info = new BDictionary
        {
            ["name"] = new BString("broken-torrent"),
            ["piece length"] = new BNumber(16384),
            ["length"] = new BNumber(1024)
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("missing or invalid 'pieces'"));
    }

    [Test]
    public void Parse_should_throw_when_file_tree_is_not_dictionary()
    {
        var info = new BDictionary
        {
            ["name"] = new BString("bad-file-tree"),
            ["piece length"] = new BNumber(16384),
            ["meta version"] = new BNumber(2),
            ["file tree"] = new BString("not a dictionary")
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("'file tree' is not a dictionary"));
    }

    [Test]
    public void Parse_should_throw_when_file_tree_leaf_missing_length()
    {
        var root = new byte[32];
        var info = new BDictionary
        {
            ["name"] = new BString("missing-length"),
            ["piece length"] = new BNumber(16384),
            ["file tree"] = new BDictionary
            {
                ["file.txt"] = new BDictionary
                {
                    [""] = new BDictionary
                    {
                        ["pieces root"] = new BString(root)
                    }
                }
            }
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("missing or invalid 'length'"));
    }

    [Test]
    public void Parse_should_throw_when_file_tree_leaf_has_negative_length()
    {
        var root = new byte[32];
        var info = new BDictionary
        {
            ["name"] = new BString("negative-length"),
            ["piece length"] = new BNumber(16384),
            ["file tree"] = new BDictionary
            {
                ["file.txt"] = new BDictionary
                {
                    [""] = new BDictionary
                    {
                        ["length"] = new BNumber(-10),
                        ["pieces root"] = new BString(root)
                    }
                }
            }
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("negative file length"));
    }

    [Test]
    public void Parse_should_throw_when_file_tree_non_empty_file_missing_pieces_root()
    {
        var info = new BDictionary
        {
            ["name"] = new BString("missing-root"),
            ["piece length"] = new BNumber(16384),
            ["file tree"] = new BDictionary
            {
                ["file.txt"] = new BDictionary
                {
                    [""] = new BDictionary
                    {
                        ["length"] = new BNumber(1024)
                    }
                }
            }
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("missing 'pieces root'"));
    }

    [Test]
    public void Parse_should_throw_when_file_tree_pieces_root_not_32_bytes()
    {
        var badRoot = new byte[20];
        var info = new BDictionary
        {
            ["name"] = new BString("short-root"),
            ["piece length"] = new BNumber(16384),
            ["file tree"] = new BDictionary
            {
                ["file.txt"] = new BDictionary
                {
                    [""] = new BDictionary
                    {
                        ["length"] = new BNumber(1024),
                        ["pieces root"] = new BString(badRoot)
                    }
                }
            }
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("'pieces root' must be 32 bytes"));
    }

    [Test]
    public void Parse_should_throw_when_file_tree_has_no_files()
    {
        var info = new BDictionary
        {
            ["name"] = new BString("empty-tree"),
            ["piece length"] = new BNumber(16384),
            ["file tree"] = new BDictionary()
        };

        var torrentDict = new BDictionary { ["info"] = info };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("contains no files"));
    }

    [Test]
    public void Parse_should_throw_when_piece_layers_hashes_not_multiple_of_32()
    {
        var rootHash = new byte[32];
        var badHashes = new byte[35];

        var fileTree = new BDictionary
        {
            ["file.dat"] = new BDictionary
            {
                [""] = new BDictionary
                {
                    ["length"] = new BNumber(32768),
                    ["pieces root"] = new BString(rootHash)
                }
            }
        };

        var info = new BDictionary
        {
            ["name"] = new BString("bad-layers"),
            ["piece length"] = new BNumber(16384),
            ["file tree"] = fileTree
        };

        var torrentDict = new BDictionary
        {
            ["info"] = info,
            ["piece layers"] = new BDictionary
            {
                [new BString(rootHash)] = new BString(badHashes)
            }
        };

        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("multiple of 32 bytes"));
    }

    [Test]
    public void Parse_should_filter_invalid_or_non_http_urls_in_url_list()
    {
        var torrentDict = CreateMinimalTorrent("test.iso");
        torrentDict["url-list"] = new BList
        {
            new BString("https://webseed.example.com/files/"),
            new BString("ftp://mirror.example.com/files/"),
            new BString("javascript:alert(1)"),
            new BString("relative/path/file.iso"),
            new BString(""),
            new BString("https://webseed.example.com/files/") // duplicate
        };

        using var stream = CreateTorrentStream(torrentDict);
        var result = _subject.Parse(stream);

        Assert.That(result.UrlList, Has.Count.EqualTo(2));
        Assert.That(result.UrlList[0], Is.EqualTo("https://webseed.example.com/files/"));
        Assert.That(result.UrlList[1], Is.EqualTo("ftp://mirror.example.com/files/"));
    }

    [Test]
    public void Parse_should_extract_nested_lists_in_url_list()
    {
        var torrentDict = CreateMinimalTorrent("test.iso");
        torrentDict["url-list"] = new BList
        {
            new BList
            {
                new BString("https://tier1.example.com/files/"),
                new BString("https://tier1-backup.example.com/files/")
            },
            new BString("https://tier2.example.com/files/")
        };

        using var stream = CreateTorrentStream(torrentDict);
        var result = _subject.Parse(stream);

        Assert.That(result.UrlList, Has.Count.EqualTo(3));
        Assert.That(result.UrlList[0], Is.EqualTo("https://tier1.example.com/files/"));
        Assert.That(result.UrlList[1], Is.EqualTo("https://tier1-backup.example.com/files/"));
        Assert.That(result.UrlList[2], Is.EqualTo("https://tier2.example.com/files/"));
    }

    [Test]
    public void ResolveWebSeedUrl_should_resolve_single_file_url_correctly()
    {
        // Trailing slash -> appends escaped filename
        var urlWithSlash = TorrentFileParser.ResolveWebSeedUrl("https://webseed.example.com/files/", "Ubuntu 22.04 [Desktop].iso");
        Assert.That(urlWithSlash, Is.EqualTo("https://webseed.example.com/files/Ubuntu%2022.04%20%5BDesktop%5D.iso"));

        // No trailing slash -> uses base URL directly
        var urlWithoutSlash = TorrentFileParser.ResolveWebSeedUrl("https://webseed.example.com/files/ubuntu.iso", "Ubuntu 22.04 [Desktop].iso");
        Assert.That(urlWithoutSlash, Is.EqualTo("https://webseed.example.com/files/ubuntu.iso"));
    }

    [Test]
    public void ResolveWebSeedUrl_should_resolve_multi_file_url_correctly()
    {
        // Trailing slash -> appends relative path with escaped segments
        var urlWithSlash = TorrentFileParser.ResolveWebSeedUrl(
            "https://webseed.example.com/downloads/",
            "TorrentRoot",
            "Music/Band - Album [FLAC]/01. Track.flac");
        Assert.That(urlWithSlash, Is.EqualTo("https://webseed.example.com/downloads/Music/Band%20-%20Album%20%5BFLAC%5D/01.%20Track.flac"));

        // No trailing slash -> treated as single concatenated file or returns base URL directly per BEP 19
        var urlWithoutSlash = TorrentFileParser.ResolveWebSeedUrl(
            "https://webseed.example.com/downloads/complete.tar",
            "TorrentRoot",
            "Music/01. Track.flac");
        Assert.That(urlWithoutSlash, Is.EqualTo("https://webseed.example.com/downloads/complete.tar"));
    }

    [Test]
    public void ResolveWebSeedUrl_should_prevent_path_traversal()
    {
        Assert.Throws<ArgumentException>(() =>
            TorrentFileParser.ResolveWebSeedUrl("https://webseed.example.com/files/", "../secret.iso"));

        Assert.Throws<ArgumentException>(() =>
            TorrentFileParser.ResolveWebSeedUrl("https://webseed.example.com/downloads/", "Root", "../etc/passwd"));
    }

    [Test]
    public void Parse_should_throw_when_multi_file_path_segment_contains_directory_traversal()
    {
        var testCases = new[]
        {
            new[] { "..", "secret.txt" },
            new[] { "folder", "..", "secret.txt" },
            new[] { "../secret.txt" },
            new[] { @"folder\..\secret.txt" },
            new[] { "dir", "..", "file.txt" }
        };

        foreach (var segments in testCases)
        {
            var pieces = new byte[20];
            new Random(42).NextBytes(pieces);

            var pathList = new BList();
            foreach (var seg in segments)
            {
                pathList.Add(new BString(seg));
            }

            var files = new BList
            {
                new BDictionary
                {
                    { "length", new BNumber(1000) },
                    { "path", pathList }
                }
            };

            var info = new BDictionary
            {
                { "name", new BString("my-torrent") },
                { "piece length", new BNumber(16384) },
                { "pieces", new BString(pieces) },
                { "files", files }
            };

            var torrentDict = new BDictionary { { "info", info } };
            using var stream = CreateTorrentStream(torrentDict);

            var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
            Assert.That(ex.Message, Does.Contain("Path traversal attempt detected"));
        }
    }

    [TestCase("../evil")]
    [TestCase(@"..\evil")]
    [TestCase("evil/..")]
    public void Parse_should_throw_when_torrent_root_dir_contains_directory_traversal(string rootName)
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1000) },
                { "path", new BList { new BString("file.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString(rootName) },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "files", files }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Path traversal attempt detected"));
    }

    [Test]
    public void Parse_should_throw_when_file_tree_contains_directory_traversal()
    {
        var leaf = new BDictionary
        {
            { "length", new BNumber(1024) },
            { "pieces root", new BString(new byte[32]) }
        };
        var inner = new BDictionary
        {
            { "", leaf }
        };
        var fileTree = new BDictionary
        {
            { "..", inner }
        };

        var info = new BDictionary
        {
            { "name", new BString("v2-torrent") },
            { "piece length", new BNumber(16384) },
            { "meta version", new BNumber(2) },
            { "file tree", fileTree }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Path traversal attempt detected"));
    }

    [Test]
    public void Parse_should_sanitize_multi_file_paths_removing_dot_and_slashes()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var files = new BList
        {
            new BDictionary
            {
                { "length", new BNumber(1000) },
                { "path", new BList { new BString("."), new BString("/sub/"), new BString("file.txt") } }
            }
        };

        var info = new BDictionary
        {
            { "name", new BString("clean-torrent") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "files", files }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(1));
        Assert.That(result.Files[0].Path, Is.EqualTo("clean-torrent/sub/file.txt"));
    }

    [TestCase(1)]
    [TestCase(19)]
    [TestCase(21)]
    [TestCase(25)]
    [TestCase(39)]
    [TestCase(41)]
    public void Parse_should_throw_when_pieces_length_is_not_multiple_of_20(int pieceBytesLength)
    {
        var pieces = new byte[pieceBytesLength];
        new Random(42).NextBytes(pieces);

        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(1024) }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("multiple of 20 bytes"));
    }

    [Test]
    public void Parse_should_throw_when_pieces_byte_array_is_empty()
    {
        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(Array.Empty<byte>()) },
            { "length", new BNumber(1024) }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Piece count 0 exceeds maximum permitted limit"));
    }

    [Test]
    public void Parse_should_throw_when_piece_length_overflows_int_max()
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber((long)int.MaxValue + 1) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(1024) }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Invalid piece length"));
    }

    [TestCase(-1L)]
    [TestCase(-16384L)]
    [TestCase(0L)]
    public void Parse_should_throw_when_piece_length_is_non_positive(long invalidPieceLength)
    {
        var pieces = new byte[20];
        new Random(42).NextBytes(pieces);

        var info = new BDictionary
        {
            { "name", new BString("test") },
            { "piece length", new BNumber(invalidPieceLength) },
            { "pieces", new BString(pieces) },
            { "length", new BNumber(1024) }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var ex = Assert.Throws<InvalidTorrentFileException>(() => _subject.Parse(stream));
        Assert.That(ex.Message, Does.Contain("Invalid piece length"));
    }

    [Test]
    public void Parse_should_populate_PieceHashes_with_raw_20_byte_multiples_from_pieces_bencode_string()
    {
        var rawPieces = new byte[60];
        new Random(42).NextBytes(rawPieces);

        var info = new BDictionary
        {
            { "name", new BString("multi-piece.iso") },
            { "piece length", new BNumber(16384) },
            { "pieces", new BString(rawPieces) },
            { "length", new BNumber(16384 * 3) }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.PieceHashes, Is.Not.Null);
        Assert.That(result.PieceHashes.Length, Is.EqualTo(60));
        Assert.That(result.PieceHashes, Is.EqualTo(rawPieces));
        Assert.That(result.PieceCount, Is.EqualTo(3));
    }
}
