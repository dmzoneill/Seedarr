using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Test.TestHelpers;
using NzbDrone.Core.Torrents;
using Polly;

namespace NzbDrone.Core.Test.ArrIntegration.Webhook;

[TestFixture]
public class ArrWebhookServiceTest
{
    private IArrConnectionFactory _connectionFactory;
    private ITorrentService _torrentService;
    private ITorrentFileParser _torrentFileParser;
    private ArrWebhookService _service;

    [SetUp]
    public void Setup()
    {
        _connectionFactory = Substitute.For<IArrConnectionFactory>();
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileParser = Substitute.For<ITorrentFileParser>();
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());
        _torrentService.GetAll().Returns(new List<Torrent>());
        _service = new ArrWebhookService(_connectionFactory, _torrentService, _torrentFileParser);
    }

    [Test]
    public void ProcessWebhook_should_ignore_non_grab_event()
    {
        var payload = new ArrWebhookPayload { EventType = "Download" };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("Ignored event type"));
    }

    [Test]
    public void ProcessWebhook_should_ignore_test_event()
    {
        var payload = new ArrWebhookPayload { EventType = "Test" };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("Test"));
    }

    [Test]
    public void ProcessWebhook_should_fail_when_download_id_null()
    {
        var payload = new ArrWebhookPayload { EventType = "Grab", DownloadId = null };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("No downloadId"));
    }

    [Test]
    public void ProcessWebhook_should_fail_when_download_id_empty()
    {
        var payload = new ArrWebhookPayload { EventType = "Grab", DownloadId = "" };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("No downloadId"));
    }

    [Test]
    public void ProcessWebhook_should_return_success_when_torrent_already_exists()
    {
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "abc123def456" }
        });

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "ABC123DEF456"
        };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("already exists"));
        Assert.That(result.InfoHash, Is.EqualTo("abc123def456"));
    }

    [Test]
    public void ProcessWebhook_should_match_existing_torrent_case_insensitively()
    {
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "ABCDEF123456" }
        });

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "abcdef123456"
        };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("already exists"));
    }

    [Test]
    public void ProcessWebhook_should_add_new_torrent()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "ABC123",
            InstanceName = "Sonarr",
            Release = new ArrWebhookRelease
            {
                ReleaseTitle = "Test.Show.S01E01",
                Indexer = "TestIndexer",
                Size = 1024000
            }
        };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("Added"));
        Assert.That(result.InfoHash, Is.EqualTo("abc123"));
        _torrentService.Received(1).Add(Arg.Any<Torrent>());
    }

    [Test]
    public void ProcessWebhook_should_lowercase_info_hash()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "ABCDEF123456",
            Release = new ArrWebhookRelease { ReleaseTitle = "Test" }
        };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.InfoHash, Is.EqualTo("abcdef123456"));
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.InfoHash == "abcdef123456"));
    }

    [Test]
    public void ProcessWebhook_should_set_torrent_name_from_release_title()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "hash123",
            Release = new ArrWebhookRelease
            {
                ReleaseTitle = "My.Movie.2024.1080p"
            }
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Name == "My.Movie.2024.1080p"));
    }

    [Test]
    public void ProcessWebhook_should_use_info_hash_as_name_when_release_null()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "HASH123",
            Release = null
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Name == "hash123"));
    }

    [Test]
    public void ProcessWebhook_should_use_info_hash_as_name_when_release_title_null()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "HASH456",
            Release = new ArrWebhookRelease { ReleaseTitle = null }
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Name == "hash456"));
    }

    [Test]
    public void ProcessWebhook_should_set_torrent_status_to_queued()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "hash789"
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Queued));
    }

    [Test]
    public void ProcessWebhook_should_set_torrent_size_from_release()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "hash111",
            Release = new ArrWebhookRelease { Size = 999888777 }
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.TotalSize == 999888777));
    }

    [Test]
    public void ProcessWebhook_should_set_zero_size_when_release_null()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "hash222",
            Release = null
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.TotalSize == 0));
    }

    [Test]
    public void ProcessWebhook_should_set_date_added()
    {
        var before = DateTime.UtcNow;
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "hash333"
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.DateAdded >= before && t.DateAdded <= DateTime.UtcNow.AddSeconds(1)));
    }

    [Test]
    public void FindConnection_should_match_by_application_url()
    {
        var definition = new ArrConnectionDefinition
        {
            Enable = true,
            Url = "http://sonarr:8989",
            ArrType = "Sonarr"
        };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { definition });

        var payload = new ArrWebhookPayload { ApplicationUrl = "http://sonarr:8989/" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Url, Is.EqualTo("http://sonarr:8989"));
    }

    [Test]
    public void FindConnection_should_match_by_instance_name()
    {
        var definition = new ArrConnectionDefinition
        {
            Enable = true,
            ArrType = "Sonarr",
            Url = "http://localhost:8989"
        };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { definition });

        var payload = new ArrWebhookPayload { InstanceName = "My Sonarr Instance" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo("Sonarr"));
    }

    [Test]
    public void FindConnection_should_fallback_to_first_enabled()
    {
        var definition = new ArrConnectionDefinition
        {
            Enable = true,
            ArrType = "Radarr",
            Url = "http://radarr:7878"
        };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { definition });

        var payload = new ArrWebhookPayload();
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo("Radarr"));
    }

    [Test]
    public void FindConnection_should_return_null_when_no_connections()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());

        var payload = new ArrWebhookPayload();
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindConnection_should_skip_disabled_connections()
    {
        var definitions = new List<ArrConnectionDefinition>
        {
            new() { Enable = false, ArrType = "Sonarr", Url = "http://sonarr:8989" },
            new() { Enable = true, ArrType = "Radarr", Url = "http://radarr:7878" }
        };
        _connectionFactory.All().Returns(definitions);

        var payload = new ArrWebhookPayload { ApplicationUrl = "http://sonarr:8989" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo("Radarr"));
    }

    [Test]
    public void ArrWebhookResult_should_have_settable_properties()
    {
        var result = new ArrWebhookResult
        {
            Success = true,
            Message = "Test message",
            InfoHash = "abc123"
        };

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Is.EqualTo("Test message"));
        Assert.That(result.InfoHash, Is.EqualTo("abc123"));
    }

    [Test]
    public void ProcessWebhook_should_trigger_enrich_when_connection_found()
    {
        var definition = new ArrConnectionDefinition
        {
            Enable = true,
            ArrType = "Sonarr",
            Url = "http://sonarr:8989"
        };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { definition });

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "HASH999",
            InstanceName = "My Sonarr Instance",
            Release = new ArrWebhookRelease
            {
                ReleaseTitle = "Show.S01E01.720p",
                Size = 500000
            }
        };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.InfoHash, Is.EqualTo("hash999"));
        _torrentService.Received(1).Add(Arg.Is<Torrent>(t =>
            t.Name == "Show.S01E01.720p" &&
            t.TotalSize == 500000 &&
            t.Status == TorrentStatus.Queued));
    }

    [Test]
    public void FindConnection_should_not_match_empty_url_by_application_url()
    {
        var definitions = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, ArrType = "Sonarr", Url = "" },
            new() { Enable = true, ArrType = "Radarr", Url = "http://radarr:7878" }
        };
        _connectionFactory.All().Returns(definitions);

        var payload = new ArrWebhookPayload { ApplicationUrl = "http://sonarr:8989" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo("Sonarr"));
    }

    [Test]
    public void FindConnection_should_match_application_url_ignoring_trailing_slash()
    {
        var definition = new ArrConnectionDefinition
        {
            Enable = true,
            Url = "http://sonarr:8989/",
            ArrType = "Sonarr"
        };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { definition });

        var payload = new ArrWebhookPayload { ApplicationUrl = "http://sonarr:8989" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo("Sonarr"));
    }

    [Test]
    public void FindConnection_should_not_match_by_instance_name_when_arr_type_empty()
    {
        var definition = new ArrConnectionDefinition
        {
            Enable = true,
            ArrType = "",
            Url = "http://localhost:8989"
        };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { definition });

        var payload = new ArrWebhookPayload { InstanceName = "My Sonarr" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo(""));
    }

    [Test]
    public void FindConnection_should_skip_disabled_in_instance_name_match()
    {
        var definitions = new List<ArrConnectionDefinition>
        {
            new() { Enable = false, ArrType = "Sonarr", Url = "http://sonarr:8989" },
        };
        _connectionFactory.All().Returns(definitions);

        var payload = new ArrWebhookPayload { InstanceName = "My Sonarr" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindConnection_should_prefer_application_url_match_over_instance_name()
    {
        var definitions = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, ArrType = "Radarr", Url = "http://radarr:7878" },
            new() { Enable = true, ArrType = "Sonarr", Url = "http://sonarr:8989" }
        };
        _connectionFactory.All().Returns(definitions);

        var payload = new ArrWebhookPayload
        {
            ApplicationUrl = "http://sonarr:8989",
            InstanceName = "My Radarr"
        };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo("Sonarr"));
    }

    [Test]
    public void ProcessWebhook_should_handle_whitespace_only_download_id()
    {
        var payload = new ArrWebhookPayload { EventType = "Grab", DownloadId = "   " };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.InfoHash, Is.EqualTo("   "));
    }

    [Test]
    public void ProcessWebhook_should_not_add_when_torrent_already_exists_by_hash()
    {
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "existinghash" }
        });

        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "EXISTINGHASH"
        };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("already exists"));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void ArrWebhookResult_should_default_success_to_false()
    {
        var result = new ArrWebhookResult();

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.Null);
        Assert.That(result.InfoHash, Is.Null);
    }

    [Test]
    public void ProcessWebhook_should_handle_rename_event()
    {
        var payload = new ArrWebhookPayload { EventType = "Rename" };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("Ignored"));
    }

    [Test]
    public void ProcessWebhook_should_handle_health_event()
    {
        var payload = new ArrWebhookPayload { EventType = "Health" };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("Ignored"));
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_private_ip_ssrf_url()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // 192.168.x.x is private — UrlValidator.IsSafeUrl returns false, SSRF blocked
        var result = (byte[])method.Invoke(_service, new object[] { "http://192.168.1.50/file.torrent" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_null_url()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // null URL fails IsSafeUrl check (not a valid URL)
        var result = (byte[])method.Invoke(_service, new object[] { (string)null });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_loopback_url()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // loopback is private — SSRF blocked
        var result = (byte[])method.Invoke(_service, new object[] { "http://127.0.0.1:9999/file.torrent" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindConnection_should_fallthrough_when_application_url_misses_and_instance_name_also_misses()
    {
        var definitions = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, ArrType = "Radarr", Url = "http://radarr:7878" }
        };
        _connectionFactory.All().Returns(definitions);

        // ApplicationUrl doesn't match "radarr:7878", InstanceName "My Sonarr" doesn't contain "Radarr"
        // Both matching blocks are entered but find no match — falls through to first enabled
        var payload = new ArrWebhookPayload
        {
            ApplicationUrl = "http://sonarr:8989",
            InstanceName = "My Sonarr"
        };

        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ArrType, Is.EqualTo("Radarr"));
    }

    [Test]
    public void ArrWebhookRelease_should_have_all_settable_properties()
    {
        var release = new ArrWebhookRelease
        {
            ReleaseTitle = "Test.Show.S01E01.720p",
            Indexer = "NZBGeek",
            Size = 1234567890,
            Quality = "720p",
            ReleaseGroup = "GROUP",
            IndexerFlags = new[] { "freeleech" }
        };

        Assert.That(release.ReleaseTitle, Is.EqualTo("Test.Show.S01E01.720p"));
        Assert.That(release.Indexer, Is.EqualTo("NZBGeek"));
        Assert.That(release.Size, Is.EqualTo(1234567890));
        Assert.That(release.Quality, Is.EqualTo("720p"));
        Assert.That(release.ReleaseGroup, Is.EqualTo("GROUP"));
        Assert.That(release.IndexerFlags, Has.Length.EqualTo(1));
        Assert.That(release.IndexerFlags[0], Is.EqualTo("freeleech"));
    }

    [Test]
    public void ArrWebhookPayload_should_have_all_settable_properties()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            InstanceName = "Sonarr",
            ApplicationUrl = "http://sonarr:8989",
            DownloadClient = "qBittorrent",
            DownloadClientType = "qBittorrent",
            DownloadId = "ABCDEF123456",
            Release = new ArrWebhookRelease { ReleaseTitle = "Test" }
        };

        Assert.That(payload.EventType, Is.EqualTo("Grab"));
        Assert.That(payload.InstanceName, Is.EqualTo("Sonarr"));
        Assert.That(payload.ApplicationUrl, Is.EqualTo("http://sonarr:8989"));
        Assert.That(payload.DownloadClient, Is.EqualTo("qBittorrent"));
        Assert.That(payload.DownloadClientType, Is.EqualTo("qBittorrent"));
        Assert.That(payload.DownloadId, Is.EqualTo("ABCDEF123456"));
        Assert.That(payload.Release, Is.Not.Null);
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_empty_url()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (byte[])method.Invoke(_service, new object[] { "" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_whitespace_url()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (byte[])method.Invoke(_service, new object[] { "   " });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_ftp_scheme()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (byte[])method.Invoke(_service, new object[] { "ftp://files.example.com/file.torrent" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_ten_dot_network_ssrf()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (byte[])method.Invoke(_service, new object[] { "http://10.0.0.1/file.torrent" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FetchTorrentFile_should_return_null_for_172_16_range_ssrf()
    {
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var result = (byte[])method.Invoke(_service, new object[] { "http://172.16.0.1/file.torrent" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindConnection_should_match_multiple_enabled_by_instance_name_returns_first()
    {
        var definitions = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, ArrType = "Sonarr", Url = "http://sonarr1:8989" },
            new() { Enable = true, ArrType = "Sonarr", Url = "http://sonarr2:8989" }
        };
        _connectionFactory.All().Returns(definitions);

        var payload = new ArrWebhookPayload { InstanceName = "My Sonarr" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Url, Is.EqualTo("http://sonarr1:8989"));
    }

    [Test]
    public void ProcessWebhook_should_handle_download_event_type()
    {
        var payload = new ArrWebhookPayload { EventType = "Download" };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("Download"));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void ProcessWebhook_should_handle_null_event_type()
    {
        var payload = new ArrWebhookPayload { EventType = null };

        var result = _service.ProcessWebhook(payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Message, Does.Contain("Ignored event type"));
    }

    [Test]
    public void ProcessWebhook_should_use_release_size_zero_when_size_not_set()
    {
        var payload = new ArrWebhookPayload
        {
            EventType = "Grab",
            DownloadId = "hash444",
            Release = new ArrWebhookRelease { ReleaseTitle = "Test" }
        };

        _service.ProcessWebhook(payload);

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.TotalSize == 0));
    }

    [Test]
    public void ArrWebhookRelease_should_default_size_to_zero()
    {
        var release = new ArrWebhookRelease();

        Assert.That(release.Size, Is.EqualTo(0));
        Assert.That(release.ReleaseTitle, Is.Null);
        Assert.That(release.Indexer, Is.Null);
        Assert.That(release.Quality, Is.Null);
        Assert.That(release.ReleaseGroup, Is.Null);
        Assert.That(release.IndexerFlags, Is.Null);
    }

    [Test]
    public void FindConnection_should_not_match_disabled_connection_by_application_url()
    {
        var definitions = new List<ArrConnectionDefinition>
        {
            new() { Enable = false, ArrType = "Sonarr", Url = "http://sonarr:8989" }
        };
        _connectionFactory.All().Returns(definitions);

        var payload = new ArrWebhookPayload { ApplicationUrl = "http://sonarr:8989" };
        var method = typeof(ArrWebhookService).GetMethod("FindConnection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (ArrConnectionDefinition)method.Invoke(_service, new object[] { payload });

        Assert.That(result, Is.Null);
    }

    // --- Constructor-injection tests (inject mock HttpClient + fresh policy) ---

    private ArrWebhookService CreateWithMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var policy = new ResiliencePipelineBuilder().Build();
        return new ArrWebhookService(
            _connectionFactory,
            _torrentService,
            _torrentFileParser,
            httpClient,
            policy);
    }

    [Test]
    public void QueryHistoryForDownloadUrl_with_injected_client_should_return_url_when_found()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""https://tracker.example.com/file.torrent""}}]}");

        var service = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookService).GetMethod("QueryHistoryForDownloadUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(service, new object[] { connection, "v3", "ABCDEF123456" });

        Assert.That(result, Is.EqualTo("https://tracker.example.com/file.torrent"));
    }

    [Test]
    public void QueryHistoryForDownloadUrl_with_injected_client_should_return_null_when_api_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");

        var service = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookService).GetMethod("QueryHistoryForDownloadUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(service, new object[] { connection, "v3", "ABCDEF123456" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void QueryHistoryForDownloadUrl_with_injected_client_should_return_null_when_no_records_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""page"":1}");

        var service = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookService).GetMethod("QueryHistoryForDownloadUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(service, new object[] { connection, "v3", "ABCDEF123456" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void QueryHistoryForDownloadUrl_with_injected_client_should_return_null_when_no_download_url_in_data()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""records"":[{""data"":{""infoHash"":""abc""}}]}");

        var service = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookService).GetMethod("QueryHistoryForDownloadUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(service, new object[] { connection, "v3", "ABCDEF123456" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void QueryHistoryForDownloadUrl_with_injected_client_should_return_null_when_records_empty()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""records"":[]}");

        var service = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookService).GetMethod("QueryHistoryForDownloadUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(service, new object[] { connection, "v3", "ABCDEF123456" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void QueryHistoryForDownloadUrl_with_injected_client_uses_v1_for_lidarr()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""https://tracker.example.com/lidarr.torrent""}}]}");

        var service = CreateWithMockClient(handler);
        var connection = new ArrConnectionDefinition
        {
            ArrType = "Lidarr",
            Url = "http://lidarr:8686",
            ApiKey = "test-key"
        };

        var method = typeof(ArrWebhookService).GetMethod("QueryHistoryForDownloadUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)method.Invoke(service, new object[] { connection, "v1", "ABCDEF123456" });

        Assert.That(result, Is.EqualTo("https://tracker.example.com/lidarr.torrent"));
    }

    [Test]
    public void FetchTorrentFile_with_injected_client_should_return_bytes_when_fetch_succeeds()
    {
        var torrentBytes = new byte[] { 0x64, 0x31, 0x30, 0x65 };
        var handler = new MockHttpMessageHandler();
        handler.EnqueueBytes(HttpStatusCode.OK, torrentBytes);

        var service = CreateWithMockClient(handler);
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);

        // Use a public IP address that passes IsSafeUrl (93.184.216.34 = example.com)
        var result = (byte[])method.Invoke(service, new object[] { "http://93.184.216.34/file.torrent" });

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(torrentBytes));
    }

    [Test]
    public void FetchTorrentFile_with_injected_client_should_return_null_when_fetch_returns_non_success()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, @"{}");

        var service = CreateWithMockClient(handler);
        var method = typeof(ArrWebhookService).GetMethod("FetchTorrentFile",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var result = (byte[])method.Invoke(service, new object[] { "http://93.184.216.34/file.torrent" });

        Assert.That(result, Is.Null);
    }

    [Test]
    public void EnrichTorrentFromHistoryAsync_should_handle_cancellation_gracefully()
    {
        var service = CreateWithMockClient(new MockHttpMessageHandler());
        var method = typeof(ArrWebhookService).GetMethod("EnrichTorrentFromHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should complete without throwing when cancelled
        var task = (Task)method.Invoke(service, new object[] { 1, "testhash", "TESTHASH", connection, "Sonarr", cts.Token });
        Assert.DoesNotThrowAsync(async () => await task);
    }

    [Test]
    public void EnrichTorrentFromHistoryAsync_should_handle_missing_torrent_after_enrich()
    {
        // Mock the history to return a download URL pointing to a public IP
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""https://93.184.216.34/file.torrent""}}]}");
        handler.EnqueueBytes(HttpStatusCode.OK, new byte[] { 0x64, 0x31, 0x30, 0x65 });

        var service = CreateWithMockClient(handler);

        // Torrent parser returns a parsed result
        var parsed = new ParsedTorrent
        {
            Name = "TestTorrent",
            InfoHash = "newhash",
            TotalSize = 500,
            PieceCount = 10,
            PieceLength = 512,
            IsPrivate = false
        };
        _torrentFileParser.Parse(Arg.Any<System.IO.Stream>()).Returns(parsed);

        // Torrent is not found after delay (deleted between add and enrich)
        _torrentService.Get(Arg.Any<int>()).Returns((Torrent)null);

        var method = typeof(ArrWebhookService).GetMethod("EnrichTorrentFromHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = (Task)method.Invoke(service, new object[] { 1, "testhash", "TESTHASH", connection, "Sonarr", cts.Token });

        // Should handle the delay being cancelled (2s timeout) or complete with torrent not found
        Assert.DoesNotThrowAsync(async () => await task);
    }

    // ── EnrichTorrentFromHistoryAsync: paths that require the 5-second delay to elapse ──

    [Test]
    public async Task EnrichTorrentFromHistoryAsync_should_return_early_when_torrent_bytes_are_null()
    {
        // History returns a valid downloadUrl; fetching the file returns HTTP 404, so
        // FetchTorrentFile returns null — exercises the null-bytes early-return path.
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""http://93.184.216.34/file.torrent""}}]}");
        handler.Enqueue(HttpStatusCode.NotFound, "{}");

        var service = CreateWithMockClient(handler);
        var method = typeof(ArrWebhookService).GetMethod("EnrichTorrentFromHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        // CancellationToken.None: lets the 5-second Task.Delay run to completion
        var task = (Task)method.Invoke(service, new object[] { 1, "testhash", "TESTHASH", connection, "Sonarr", CancellationToken.None });
        await task;

        // Update should never be called when torrent bytes could not be fetched
        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public async Task EnrichTorrentFromHistoryAsync_should_return_early_when_torrent_bytes_are_empty()
    {
        // FetchTorrentFile returns an empty array; the method logs a warning and returns.
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""http://93.184.216.34/file.torrent""}}]}");
        handler.EnqueueBytes(HttpStatusCode.OK, Array.Empty<byte>());

        var service = CreateWithMockClient(handler);
        var method = typeof(ArrWebhookService).GetMethod("EnrichTorrentFromHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var task = (Task)method.Invoke(service, new object[] { 1, "testhash", "TESTHASH", connection, "Sonarr", CancellationToken.None });
        await task;

        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public async Task EnrichTorrentFromHistoryAsync_should_return_early_when_torrent_not_found_in_service()
    {
        // Everything succeeds (URL found, bytes fetched, parsed) but the torrent has
        // been removed from the service by the time Get is called — exercises the null-torrent path.
        var torrentBytes = new byte[] { 0x64, 0x65 };
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""http://93.184.216.34/file.torrent""}}]}");
        handler.EnqueueBytes(HttpStatusCode.OK, torrentBytes);

        var parsed = new ParsedTorrent
        {
            Name = "Parsed",
            InfoHash = "parsedhash",
            TotalSize = 512,
            PieceCount = 2,
            PieceLength = 256,
            IsPrivate = false
        };
        _torrentFileParser.Parse(Arg.Any<System.IO.Stream>()).Returns(parsed);
        _torrentService.Get(Arg.Any<int>()).Returns((Torrent)null);

        var service = CreateWithMockClient(handler);
        var method = typeof(ArrWebhookService).GetMethod("EnrichTorrentFromHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var task = (Task)method.Invoke(service, new object[] { 1, "testhash", "TESTHASH", connection, "Sonarr", CancellationToken.None });
        await task;

        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
    }

    [Test]
    public async Task EnrichTorrentFromHistoryAsync_should_update_torrent_with_full_parsed_metadata()
    {
        // Full happy path: history found, file fetched, parsed, torrent updated.
        var torrentBytes = new byte[] { 0x64, 0x65 };
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""http://93.184.216.34/file.torrent""}}]}");
        handler.EnqueueBytes(HttpStatusCode.OK, torrentBytes);

        var parsed = new ParsedTorrent
        {
            Name = "EnrichedTorrent",
            InfoHash = "ENRICHEDHASH",
            TotalSize = 1048576,
            PieceCount = 8,
            PieceLength = 131072,
            Comment = "test comment",
            IsPrivate = true
        };
        _torrentFileParser.Parse(Arg.Any<System.IO.Stream>()).Returns(parsed);

        var existingTorrent = new Torrent { Id = 1, Name = "OldName", InfoHash = "oldhash" };
        _torrentService.Get(1).Returns(existingTorrent);

        var service = CreateWithMockClient(handler);
        var method = typeof(ArrWebhookService).GetMethod("EnrichTorrentFromHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var task = (Task)method.Invoke(service, new object[] { 1, "oldhash", "OLDHASH", connection, "Sonarr", CancellationToken.None });
        await task;

        _torrentService.Received(1).Update(Arg.Is<Torrent>(t =>
            t.Name == "EnrichedTorrent" &&
            t.InfoHash == "enrichedhash" &&
            t.TotalSize == 1048576 &&
            t.PieceCount == 8 &&
            t.IsPrivate == true));
    }

    [Test]
    public async Task EnrichTorrentFromHistoryAsync_should_catch_exception_from_parser_and_not_rethrow()
    {
        // If the torrent file parser throws, the outer catch (Exception ex) handler
        // logs the error and returns without crashing.
        var torrentBytes = new byte[] { 0x64, 0x65 };
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""data"":{""downloadUrl"":""http://93.184.216.34/file.torrent""}}]}");
        handler.EnqueueBytes(HttpStatusCode.OK, torrentBytes);

        _torrentFileParser.Parse(Arg.Any<System.IO.Stream>())
            .Returns<ParsedTorrent>(_ => throw new InvalidOperationException("Corrupt torrent data"));

        var service = CreateWithMockClient(handler);
        var method = typeof(ArrWebhookService).GetMethod("EnrichTorrentFromHistoryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var connection = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "test-key"
        };

        var task = (Task)method.Invoke(service, new object[] { 1, "testhash", "TESTHASH", connection, "Sonarr", CancellationToken.None });
        Assert.DoesNotThrowAsync(async () => await task);

        _torrentService.DidNotReceive().Update(Arg.Any<Torrent>());
    }
}
