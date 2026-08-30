using System;
using System.IO;
using BencodeNET.Objects;
using NUnit.Framework;
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

    private static BDictionary CreateMinimalTorrent(string name = "test-file.txt", long fileSize = 1024, int pieceLength = 512)
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
            { "piece length", new BNumber(512) },
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
            { "piece length", new BNumber(512) },
            { "pieces", new BString(pieces) },
            { "files", files }
        };

        var torrentDict = new BDictionary { { "info", info } };
        using var stream = CreateTorrentStream(torrentDict);

        var result = _subject.Parse(stream);

        Assert.That(result.Files, Has.Count.EqualTo(2));
        Assert.That(result.Files[0].Path, Is.EqualTo("folder/file1.txt"));
        Assert.That(result.Files[0].Size, Is.EqualTo(1000));
        Assert.That(result.Files[1].Path, Is.EqualTo("folder/file2.txt"));
        Assert.That(result.Files[1].Size, Is.EqualTo(2000));
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
            { "piece length", new BNumber(512) },
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
}
