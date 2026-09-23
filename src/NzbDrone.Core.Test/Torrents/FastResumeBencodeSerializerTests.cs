using System;
using System.Collections.Generic;
using System.Text;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class FastResumeBencodeSerializerTests
{
    private FastResumeBencodeSerializer _serializer;

    [SetUp]
    public void SetUp()
    {
        _serializer = new FastResumeBencodeSerializer();
    }

    [Test]
    public void IsBencode_returns_false_for_null_or_empty_or_short_bytes()
    {
        Assert.That(_serializer.IsBencode(null), Is.False);
        Assert.That(_serializer.IsBencode(Array.Empty<byte>()), Is.False);
        Assert.That(_serializer.IsBencode(new byte[] { (byte)'d' }), Is.False);
    }

    [Test]
    public void IsBencode_returns_true_for_dictionary_bencode()
    {
        var validDict = Encoding.UTF8.GetBytes("d4:name4:teste");
        Assert.That(_serializer.IsBencode(validDict), Is.True);

        var whitespacePrefixed = Encoding.UTF8.GetBytes("   \r\n\t d4:name4:teste");
        Assert.That(_serializer.IsBencode(whitespacePrefixed), Is.True);
    }

    [Test]
    public void IsBencode_returns_false_for_non_dict_bencode_or_plain_text()
    {
        var listBencode = Encoding.UTF8.GetBytes("l4:spame");
        Assert.That(_serializer.IsBencode(listBencode), Is.False);

        var intBencode = Encoding.UTF8.GetBytes("i42e");
        Assert.That(_serializer.IsBencode(intBencode), Is.False);

        var stringBencode = Encoding.UTF8.GetBytes("4:spam");
        Assert.That(_serializer.IsBencode(stringBencode), Is.False);

        var plainText = Encoding.UTF8.GetBytes("Hello World!");
        Assert.That(_serializer.IsBencode(plainText), Is.False);

        var whitespaceOnly = Encoding.UTF8.GetBytes("    \t\r\n");
        Assert.That(_serializer.IsBencode(whitespaceOnly), Is.False);
    }

    [Test]
    public void Serialize_throws_argument_null_exception_when_data_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => _serializer.Serialize(null));
    }

    [Test]
    public void Serialize_applies_default_values_when_fields_are_empty()
    {
        var data = new FastResumeData();
        var bytes = _serializer.Serialize(data);

        Assert.That(bytes, Is.Not.Null.And.Not.Empty);

        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(bytes);

        Assert.That(dict["file-format"].ToString(), Is.EqualTo(FastResumeBencodeSerializer.DefaultFileFormat));
        Assert.That(((BNumber)dict["file-version"]).Value, Is.EqualTo(FastResumeBencodeSerializer.DefaultFileVersion));
        Assert.That(((BString)dict["allocation"]).ToString(Encoding.UTF8), Is.EqualTo("sparse"));
        Assert.That(((BString)dict["info-hash"]).Value.Length, Is.EqualTo(20));
        Assert.That(((BNumber)dict["state"]).Value, Is.EqualTo(3));
        Assert.That(((BNumber)dict["qBt-seedStatus"]).Value, Is.EqualTo(0));
    }

    [Test]
    public void Serialize_encodes_hex_infohash_into_raw_20_bytes()
    {
        var hexHash = "0123456789abcdef0123456789abcdef01234567";
        var data = new FastResumeData
        {
            InfoHash = hexHash
        };

        var bytes = _serializer.Serialize(data);
        var dict = new BencodeParser().Parse<BDictionary>(bytes);
        var hashBytes = ((BString)dict["info-hash"]).Value.ToArray();

        Assert.That(hashBytes.Length, Is.EqualTo(20));
        Assert.That(Convert.ToHexString(hashBytes).ToLowerInvariant(), Is.EqualTo(hexHash.ToLowerInvariant()));
    }

    [Test]
    public void Serialize_handles_non_40_char_or_invalid_hex_infohash()
    {
        var shortHash = "short_hash";
        var data = new FastResumeData
        {
            InfoHash = shortHash
        };

        var bytes = _serializer.Serialize(data);
        var dict = new BencodeParser().Parse<BDictionary>(bytes);
        var hashBytes = ((BString)dict["info-hash"]).Value.ToArray();

        Assert.That(hashBytes.Length, Is.EqualTo(20));
        Assert.That(Encoding.UTF8.GetString(hashBytes, 0, shortHash.Length), Is.EqualTo(shortHash));
    }

    [Test]
    public void Serialize_encodes_bitfield_and_piece_priorities()
    {
        var data = new FastResumeData
        {
            Bitfield = new[] { true, false, true, true, false }
        };

        var bytes = _serializer.Serialize(data);
        var dict = new BencodeParser().Parse<BDictionary>(bytes);

        var piecesBytes = ((BString)dict["pieces"]).Value.ToArray();
        Assert.That(piecesBytes, Is.EqualTo(new byte[] { 1, 0, 1, 1, 0 }));
        Assert.That(((BNumber)dict["num_pieces"]).Value, Is.EqualTo(5));
        Assert.That(((BNumber)dict["num_downloaded"]).Value, Is.EqualTo(3));

        var ppBytes = ((BString)dict["piece_priority"]).Value.ToArray();
        Assert.That(ppBytes, Is.EqualTo(new byte[] { 1, 1, 1, 1, 1 }));
    }

    [Test]
    public void Serialize_encodes_seeding_state_when_progress_complete_or_status_seeding()
    {
        var seedingByProgress = new FastResumeData
        {
            Progress = 1.0,
            Status = "Downloading"
        };
        var bytes1 = _serializer.Serialize(seedingByProgress);
        var dict1 = new BencodeParser().Parse<BDictionary>(bytes1);
        Assert.That(((BNumber)dict1["state"]).Value, Is.EqualTo(5));
        Assert.That(((BNumber)dict1["qBt-seedStatus"]).Value, Is.EqualTo(1));

        var seedingByStatus = new FastResumeData
        {
            Progress = 0.5,
            Status = "Seeding"
        };
        var bytes2 = _serializer.Serialize(seedingByStatus);
        var dict2 = new BencodeParser().Parse<BDictionary>(bytes2);
        Assert.That(((BNumber)dict2["state"]).Value, Is.EqualTo(5));
        Assert.That(((BNumber)dict2["qBt-seedStatus"]).Value, Is.EqualTo(1));
    }

    [Test]
    public void Serialize_encodes_files_priorities_and_unfinished_pieces()
    {
        var mtime = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var data = new FastResumeData
        {
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "folder/file1.mkv", Length = 1048576, Mtime = mtime },
                new() { Path = "folder/file2.nfo", Length = 2048, Mtime = null }
            },
            Unfinished = new List<FastResumeUnfinishedPiece>
            {
                new() { Piece = 4, Bitmask = new byte[] { 0xAA, 0x55 }, Adler32 = 0x12345678 }
            },
            FilePriority = new List<int> { 1, 7 },
            PiecePriority = new byte[] { 2, 4, 6 },
            SequentialDownload = true
        };

        var bytes = _serializer.Serialize(data);
        var dict = new BencodeParser().Parse<BDictionary>(bytes);

        var fileSizes = (BList)dict["file sizes"];
        Assert.That(fileSizes.Count, Is.EqualTo(2));
        Assert.That(((BNumber)fileSizes[0]).Value, Is.EqualTo(1048576));
        Assert.That(((BNumber)fileSizes[1]).Value, Is.EqualTo(2048));

        var mappedFiles = (BList)dict["mapped_files"];
        Assert.That(((BString)mappedFiles[0]).ToString(Encoding.UTF8), Is.EqualTo("folder/file1.mkv"));

        var unfinished = (BList)dict["unfinished"];
        Assert.That(unfinished.Count, Is.EqualTo(1));
        var ufDict = (BDictionary)unfinished[0];
        Assert.That(((BNumber)ufDict["piece"]).Value, Is.EqualTo(4));
        Assert.That(((BNumber)ufDict["adler32"]).Value, Is.EqualTo(0x12345678));

        Assert.That(((BNumber)dict["sequential_download"]).Value, Is.EqualTo(1));
    }

    [Test]
    public void Deserialize_throws_argument_null_exception_for_null_or_empty_bytes()
    {
        Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize(null));
        Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize(Array.Empty<byte>()));
    }

    [Test]
    public void Deserialize_throws_for_corrupt_bencode()
    {
        var corruptBytes = Encoding.UTF8.GetBytes("d10:corrupted_bytes_without_end");
        Assert.Throws<Exception>(() => _serializer.Deserialize(corruptBytes));
    }

    [Test]
    public void Roundtrip_serialization_preserves_all_core_data()
    {
        var hexHash = "0123456789abcdef0123456789abcdef01234567";
        var mtime = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var original = new FastResumeData
        {
            FileFormat = "libtorrent resume file",
            FileVersion = 1,
            InfoHash = hexHash,
            Bitfield = new[] { true, true, false, true },
            Uploaded = 5000000,
            Downloaded = 2500000,
            SavePath = "/downloads/torrents",
            Allocation = "full",
            ActiveTime = 3600,
            SeedingTime = 1800,
            FinishedTime = 1700000000,
            SequentialDownload = true,
            Files = new List<FastResumeFileEntry>
            {
                new() { Path = "movie.mkv", Length = 2000000, Mtime = mtime }
            },
            Unfinished = new List<FastResumeUnfinishedPiece>
            {
                new() { Piece = 2, Bitmask = new byte[] { 0xFF }, Adler32 = 42 }
            }
        };

        var bytes = _serializer.Serialize(original);
        var restored = _serializer.Deserialize(bytes);

        Assert.That(restored.InfoHash, Is.EqualTo(hexHash));
        Assert.That(restored.Bitfield, Is.EqualTo(original.Bitfield));
        Assert.That(restored.Uploaded, Is.EqualTo(original.Uploaded));
        Assert.That(restored.Downloaded, Is.EqualTo(original.Downloaded));
        Assert.That(restored.SavePath, Is.EqualTo(original.SavePath));
        Assert.That(restored.Allocation, Is.EqualTo(original.Allocation));
        Assert.That(restored.ActiveTime, Is.EqualTo(original.ActiveTime));
        Assert.That(restored.SeedingTime, Is.EqualTo(original.SeedingTime));
        Assert.That(restored.FinishedTime, Is.EqualTo(original.FinishedTime));
        Assert.That(restored.SequentialDownload, Is.True);
        Assert.That(restored.SavedAt, Is.Not.Null);

        Assert.That(restored.Files.Count, Is.EqualTo(1));
        Assert.That(restored.Files[0].Path, Is.EqualTo("movie.mkv"));
        Assert.That(restored.Files[0].Length, Is.EqualTo(2000000));
        Assert.That(restored.Files[0].Mtime, Is.EqualTo(mtime));

        Assert.That(restored.Unfinished.Count, Is.EqualTo(1));
        Assert.That(restored.Unfinished[0].Piece, Is.EqualTo(2));
        Assert.That(restored.Unfinished[0].Adler32, Is.EqualTo(42));
    }

    [Test]
    public void Deserialize_handles_missing_keys_gracefully()
    {
        var minimalDict = new BDictionary();
        var bytes = minimalDict.EncodeAsBytes();

        var data = _serializer.Deserialize(bytes);

        Assert.That(data, Is.Not.Null);
        Assert.That(data.FileFormat, Is.EqualTo("libtorrent resume file"));
        Assert.That(data.FileVersion, Is.EqualTo(1));
        Assert.That(data.InfoHash, Is.Null);
        Assert.That(data.Bitfield, Is.Null);
        Assert.That(data.Files, Is.Empty);
        Assert.That(data.Unfinished, Is.Empty);
    }

    [Test]
    public void Deserialize_falls_back_to_qBt_savePath_when_save_path_missing()
    {
        var dict = new BDictionary
        {
            ["qBt-savePath"] = new BString("/qbittorrent/path")
        };

        var data = _serializer.Deserialize(dict.EncodeAsBytes());

        Assert.That(data.SavePath, Is.EqualTo("/qbittorrent/path"));
    }

    [Test]
    public void Deserialize_falls_back_to_qBt_name_when_single_file_path_is_empty()
    {
        var dict = new BDictionary
        {
            ["file sizes"] = new BList { (IBObject)new BNumber(1024) },
            ["mapped_files"] = new BList { (IBObject)new BString("") },
            ["qBt-name"] = new BString("downloaded_file.mkv")
        };

        var data = _serializer.Deserialize(dict.EncodeAsBytes());

        Assert.That(data.Files.Count, Is.EqualTo(1));
        Assert.That(data.Files[0].Path, Is.EqualTo("downloaded_file.mkv"));
    }

    [Test]
    public void Deserialize_decodes_packed_bitfield_when_byte_values_exceed_3()
    {
        // 0b10100000 = 160 decimal
        var dict = new BDictionary
        {
            ["file-version"] = new BNumber(2),
            ["pieces"] = new BString(new byte[] { 160 })
        };

        var data = _serializer.Deserialize(dict.EncodeAsBytes());

        Assert.That(data.Bitfield.Length, Is.EqualTo(8));
        Assert.That(data.Bitfield[0], Is.True);
        Assert.That(data.Bitfield[1], Is.False);
        Assert.That(data.Bitfield[2], Is.True);
        Assert.That(data.Bitfield[3], Is.False);
        Assert.That(data.Bitfield[4], Is.False);
        Assert.That(data.Bitfield[5], Is.False);
        Assert.That(data.Bitfield[6], Is.False);
        Assert.That(data.Bitfield[7], Is.False);
    }

    [Test]
    public void Static_methods_SerializeToBytes_and_DeserializeFromBytes_work_identically()
    {
        var original = new FastResumeData
        {
            InfoHash = "1111111111111111111111111111111111111111",
            Bitfield = new[] { true, false }
        };

        var bytes = FastResumeBencodeSerializer.SerializeToBytes(original);
        var deserialized = FastResumeBencodeSerializer.DeserializeFromBytes(bytes);

        Assert.That(deserialized.InfoHash, Is.EqualTo(original.InfoHash));
        Assert.That(deserialized.Bitfield, Is.EqualTo(original.Bitfield));
    }
}
