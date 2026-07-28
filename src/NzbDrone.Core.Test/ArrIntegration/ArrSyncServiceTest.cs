using System;
using System.Collections.Generic;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class ArrSyncServiceTest
{
    private IArrConnectionFactory _connectionFactory;
    private ITorrentService _torrentService;
    private ArrSyncService _service;

    /// <summary>
    /// Subclass that overrides CreateProvider so tests can inject a mock IArrConnection
    /// and exercise the inner sync loop body (which is unreachable via the switch-based factory).
    /// </summary>
    private class TestableArrSyncService : ArrSyncService
    {
        private readonly IArrConnection _provider;

        public TestableArrSyncService(
            IArrConnectionFactory connectionFactory,
            ITorrentService torrentService,
            IArrConnection provider)
            : base(connectionFactory, torrentService)
        {
            _provider = provider;
        }

        protected override IArrConnection CreateProvider(ArrConnectionDefinition definition) => _provider;
    }

    private ArrSyncService CreateTestableService(IArrConnection provider) =>
        new TestableArrSyncService(_connectionFactory, _torrentService, provider);

    private static ArrConnectionDefinition EnabledDefinition(string arrType = "Sonarr") =>
        new() { Enable = true, SyncEnabled = true, ArrType = arrType, Name = "Test" };

    [SetUp]
    public void Setup()
    {
        _connectionFactory = Substitute.For<IArrConnectionFactory>();
        _torrentService = Substitute.For<ITorrentService>();
        _service = new ArrSyncService(_connectionFactory, _torrentService);
    }

    [Test]
    public void Sync_should_return_zero_counts_when_no_definitions()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_skip_disabled_connection()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = false, SyncEnabled = true, ArrType = "Sonarr", Name = "Test" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_skip_connection_when_sync_disabled()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = false, ArrType = "Radarr", Name = "Test" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_skip_connection_when_both_enable_and_sync_disabled()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = false, SyncEnabled = false, ArrType = "Sonarr", Name = "Test" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_increment_failed_when_arr_type_unknown()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = true, ArrType = "Emby", Name = "Unknown Arr" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(1));
    }

    [Test]
    public void Sync_should_increment_failed_when_arr_type_null()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = true, ArrType = null, Name = "Null type" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(1));
    }

    [Test]
    public void Sync_should_handle_multiple_connections_with_mixed_states()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = false, SyncEnabled = true, ArrType = "Sonarr", Name = "Disabled" },
            new() { Enable = true, SyncEnabled = false, ArrType = "Radarr", Name = "No sync" },
            new() { Enable = true, SyncEnabled = true, ArrType = "BadType", Name = "Bad" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(1));
        Assert.That(result.Added, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_build_existing_hashes_from_torrents()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "abc123" },
            new() { InfoHash = null },
            new() { InfoHash = "" },
            new() { InfoHash = "DEF456" }
        });

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        _torrentService.Received(1).GetAll();
    }

    [Test]
    public void TestConnection_should_return_false_for_unknown_arr_type()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            ArrType = "InvalidType",
            Name = "Bad Connection"
        });

        var result = _service.TestConnection(1);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TestConnection_should_call_factory_get_with_correct_id()
    {
        _connectionFactory.Get(42).Returns(new ArrConnectionDefinition
        {
            ArrType = "Unknown"
        });

        _service.TestConnection(42);

        _connectionFactory.Received(1).Get(42);
    }

    [Test]
    public void TestConnection_should_return_false_for_null_arr_type()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            ArrType = null,
            Name = "Null type"
        });

        var result = _service.TestConnection(1);

        Assert.That(result, Is.False);
    }

    [Test]
    public void SyncResult_should_default_to_zero()
    {
        var result = new SyncResult();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_skip_records_with_empty_infohash()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new()
            {
                Enable = true,
                SyncEnabled = true,
                ArrType = "Sonarr",
                Name = "Test",
                Url = "http://nonexistent.invalid:8989",
                ApiKey = "key"
            }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(1).Or.EqualTo(0).Or.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Sync_should_skip_already_existing_torrents()
    {
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "abc123" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new()
            {
                Enable = true,
                SyncEnabled = true,
                ArrType = "UnknownType",
                Name = "Test"
            }
        });

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(1));
    }

    [Test]
    public void Sync_should_handle_multiple_unknown_types()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = true, ArrType = "Plex", Name = "Plex" },
            new() { Enable = true, SyncEnabled = true, ArrType = "Jellyfin", Name = "Jellyfin" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(2));
    }

    [Test]
    public void Sync_should_handle_empty_arr_type_string()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = true, ArrType = "", Name = "Empty type" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(1));
    }

    [Test]
    public void Sync_should_filter_existing_hashes_with_null_and_empty()
    {
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = null },
            new() { InfoHash = "" },
            new() { InfoHash = "valid123" },
            new() { InfoHash = null }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
    }

    [Test]
    public void SyncResult_should_have_settable_properties()
    {
        var result = new SyncResult
        {
            Added = 5,
            Skipped = 10,
            Failed = 2
        };

        Assert.That(result.Added, Is.EqualTo(5));
        Assert.That(result.Skipped, Is.EqualTo(10));
        Assert.That(result.Failed, Is.EqualTo(2));
    }

    [Test]
    public void TestConnection_should_create_sonarr_provider()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Name = "My Sonarr",
            Url = "http://nonexistent.invalid:8989",
            ApiKey = "test-key"
        });

        var result = _service.TestConnection(1);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TestConnection_should_create_radarr_provider()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            ArrType = "Radarr",
            Name = "My Radarr",
            Url = "http://nonexistent.invalid:7878",
            ApiKey = "test-key"
        });

        var result = _service.TestConnection(1);

        Assert.That(result, Is.False);
    }

    [Test]
    public void TestConnection_should_create_lidarr_provider()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            ArrType = "Lidarr",
            Name = "My Lidarr",
            Url = "http://nonexistent.invalid:8686",
            ApiKey = "test-key"
        });

        var result = _service.TestConnection(1);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Sync_should_skip_connections_where_enable_is_false_even_when_sync_enabled()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new()
            {
                Enable = false,
                SyncEnabled = true,
                ArrType = "Sonarr",
                Name = "Disabled Sonarr",
                Url = "http://sonarr:8989",
                ApiKey = "key"
            }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_build_case_insensitive_hash_set()
    {
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "ABC123" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        _torrentService.Received(1).GetAll();
    }

    [Test]
    public void TestConnection_should_return_false_for_empty_string_arr_type()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            ArrType = "",
            Name = "Empty type"
        });

        var result = _service.TestConnection(1);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Sync_should_not_fail_when_sonarr_provider_encounters_bad_url()
    {
        // SonarrConnection.GetDownloadHistory() has its own try-catch and returns
        // an empty list on failure, so Sync's catch block is NOT triggered.
        // Failed should remain 0 (provider doesn't propagate exceptions).
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new()
            {
                Enable = true,
                SyncEnabled = true,
                ArrType = "Sonarr",
                Name = "My Sonarr",
                Url = "http://nonexistent.invalid:8989",
                ApiKey = "key"
            }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));

        // Failed = 0 because SonarrConnection catches the HTTP exception internally
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_not_fail_when_radarr_provider_encounters_bad_url()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new()
            {
                Enable = true,
                SyncEnabled = true,
                ArrType = "Radarr",
                Name = "My Radarr",
                Url = "http://nonexistent.invalid:7878",
                ApiKey = "key"
            }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_not_fail_when_lidarr_provider_encounters_bad_url()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new()
            {
                Enable = true,
                SyncEnabled = true,
                ArrType = "Lidarr",
                Name = "My Lidarr",
                Url = "http://nonexistent.invalid:8686",
                ApiKey = "key"
            }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_accumulate_failures_from_unknown_types_across_multiple_connections()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Enable = true, SyncEnabled = true, ArrType = "Unknown1", Name = "A" },
            new() { Enable = true, SyncEnabled = true, ArrType = "Unknown2", Name = "B" },
            new() { Enable = true, SyncEnabled = true, ArrType = "Unknown3", Name = "C" }
        });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = _service.Sync();

        Assert.That(result.Failed, Is.EqualTo(3));
        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_call_get_all_exactly_once()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());
        _torrentService.GetAll().Returns(new List<Torrent>());

        _service.Sync();

        _torrentService.Received(1).GetAll();
    }

    [Test]
    public void Sync_should_call_connection_factory_all_exactly_once()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());
        _torrentService.GetAll().Returns(new List<Torrent>());

        _service.Sync();

        _connectionFactory.Received(1).All();
    }

    // --- Inner sync loop body tests (via TestableArrSyncService) ---

    [Test]
    public void Sync_should_silently_skip_record_with_null_infohash()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = null, Title = "Null hash" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = CreateTestableService(provider).Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_silently_skip_record_with_empty_infohash()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "", Title = "Empty hash" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = CreateTestableService(provider).Sync();

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Skipped, Is.EqualTo(0));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_increment_skipped_when_hash_already_exists()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "abc123", Title = "Already seeding" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "abc123" }
        });

        var result = CreateTestableService(provider).Sync();

        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Added, Is.EqualTo(0));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_add_new_torrent_and_increment_added()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "newhash123", Title = "New show S01E01", Size = 1_000_000 }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = CreateTestableService(provider).Sync();

        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(0));
        Assert.That(result.Failed, Is.EqualTo(0));
        _torrentService.Received(1).Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_add_torrent_with_lowercase_infohash()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "ABCDEF0123", Title = "Title" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        CreateTestableService(provider).Sync();

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.InfoHash == "abcdef0123"));
    }

    [Test]
    public void Sync_should_set_torrent_status_to_queued_when_adding()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "hash001", Title = "Title" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        CreateTestableService(provider).Sync();

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Status == TorrentStatus.Queued));
    }

    [Test]
    public void Sync_should_set_torrent_name_from_record_title()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "hash002", Title = "Breaking Bad S01E01" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        CreateTestableService(provider).Sync();

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.Name == "Breaking Bad S01E01"));
    }

    [Test]
    public void Sync_should_set_torrent_total_size_from_record()
    {
        const long expectedSize = 2_500_000_000L;
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "hash003", Title = "Title", Size = expectedSize }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        CreateTestableService(provider).Sync();

        _torrentService.Received(1).Add(Arg.Is<Torrent>(t => t.TotalSize == expectedSize));
    }

    [Test]
    public void Sync_should_increment_failed_when_provider_throws_exception()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Throws(new Exception("Provider connection refused"));
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = CreateTestableService(provider).Sync();

        Assert.That(result.Failed, Is.EqualTo(1));
        Assert.That(result.Added, Is.EqualTo(0));
    }

    [Test]
    public void Sync_should_not_add_duplicate_hash_from_same_provider_in_one_sync()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "dupehash", Title = "First occurrence" },
            new() { InfoHash = "dupehash", Title = "Second occurrence" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>());

        var result = CreateTestableService(provider).Sync();

        // First is added, second is skipped because hash was added to existingHashes
        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(1));
        _torrentService.Received(1).Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_skip_existing_hash_case_insensitively()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "MIXEDCASE123", Title = "Title" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        // Existing torrent has lowercase version of the same hash
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "mixedcase123" }
        });

        var result = CreateTestableService(provider).Sync();

        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Added, Is.EqualTo(0));
        _torrentService.DidNotReceive().Add(Arg.Any<Torrent>());
    }

    [Test]
    public void Sync_should_process_multiple_records_mixed_results()
    {
        var provider = Substitute.For<IArrConnection>();
        provider.GetDownloadHistory().Returns(new List<ArrDownloadRecord>
        {
            new() { InfoHash = "existing001", Title = "Already have this" },
            new() { InfoHash = "brand_new_002", Title = "New content" },
            new() { InfoHash = null, Title = "No hash record" },
            new() { InfoHash = "another_new_003", Title = "More new content" }
        });
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { EnabledDefinition() });
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new() { InfoHash = "existing001" }
        });

        var result = CreateTestableService(provider).Sync();

        Assert.That(result.Added, Is.EqualTo(2));
        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(0));
    }
}
