// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Developer.Testing;
using NzbDrone.Core.Dht;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Developer;

[TestFixture]
public class DeveloperTestRunnerInjectedServicesTest
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_dev_runner_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Test]
    public async Task Should_execute_sqlite_database_diagnostics_with_registered_main_database()
    {
        var mainDatabase = CreateSqliteMainDatabaseMock();
        var runner = new DeveloperTestRunner(mainDatabase: mainDatabase);

        var dbTestIds = new[]
        {
            "db-integrity",
            "db-crud",
            "db-schema",
            "db-indexes",
            "db-foreign-keys",
            "db-wal-checkpoint",
            "db-autoincrement",
            "db-page-size",
            "command-definitions",
            "speed-schedules",
            "download-clients",
            "indexer-definitions",
            "notification-definitions",
        };

        foreach (var testId in dbTestIds)
        {
            var result = await runner.RunTestAsync(testId);
            Assert.That(result.Status, Is.EqualTo("Passed"), $"{testId} failed: {result.ErrorMessage}");
            Assert.That(result.Output, Is.Not.Null.And.Not.Empty, testId);
        }
    }

    [Test]
    public async Task Should_execute_postgresql_database_branches()
    {
        var mainDatabase = CreatePostgreSqlMainDatabaseMock();
        var runner = new DeveloperTestRunner(mainDatabase: mainDatabase);

        foreach (var testId in new[] { "db-integrity", "db-indexes", "db-foreign-keys", "db-wal-checkpoint", "db-autoincrement", "db-page-size" })
        {
            var result = await runner.RunTestAsync(testId);
            Assert.That(result.Status, Is.EqualTo("Passed"), testId);
            Assert.That(result.Output, Does.Contain("PostgreSQL"), testId);
        }
    }

    [Test]
    public async Task Should_execute_injected_scheduler_messaging_and_engine_checks()
    {
        var taskManager = Substitute.For<ITaskManager>();
        taskManager.GetAll().Returns(new List<ScheduledTask>
        {
            new() { TypeName = "RssSyncTask", LastExecution = DateTime.UtcNow.AddMinutes(-5) },
            new() { TypeName = "WatchFolderTask", LastExecution = default },
            new() { TypeName = "CleanupTask", LastExecution = DateTime.UtcNow },
            new() { TypeName = "BackupTask", LastExecution = DateTime.UtcNow },
        });

        var eventAggregator = Substitute.For<IEventAggregator>();
        var commandQueue = Substitute.For<IManageCommandQueue>();
        var bandwidthLimiter = Substitute.For<IBandwidthLimiter>();

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetAll().Returns(new List<Torrent> { new() { Id = 1, Name = "t1" } });

        var routingTable = new RoutingTable(new byte[20]);
        var dhtService = Substitute.For<IDhtService>();
        dhtService.RoutingTable.Returns(routingTable);

        var peerServer = Substitute.For<IPeerServer>();
        peerServer.IsListening.Returns(true);
        peerServer.ListeningPort.Returns(6882);

        var configService = Substitute.For<IConfigService>();
        configService.ListeningPort.Returns(6882);
        configService.TorrentSaveDirectory.Returns("/downloads/seedarr");

        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.EnableSsl.Returns(true);
        configFileProvider.SslPort.Returns(8443);
        configFileProvider.EnableVpnKillSwitch.Returns(true);

        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(_tempDir);

        var categoryService = Substitute.For<ICategoryService>();
        categoryService.GetAll().Returns(new[] { new Category { Id = 1, Name = "Movies" } });

        var tagRepository = Substitute.For<ITagRepository>();
        tagRepository.All().Returns(new[] { new Tag { Id = 1, Label = "1080p" } });

        var runner = new DeveloperTestRunner(
            mainDatabase: CreateSqliteMainDatabaseMock(),
            torrentService: torrentService,
            dhtService: dhtService,
            peerServer: peerServer,
            bandwidthLimiter: bandwidthLimiter,
            taskManager: taskManager,
            commandQueue: commandQueue,
            eventAggregator: eventAggregator,
            configService: configService,
            configFileProvider: configFileProvider,
            appFolderInfo: appFolderInfo,
            categoryService: categoryService,
            tagRepository: tagRepository);

        var injectedIds = new[]
        {
            "engine-status",
            "engine-ratelimits",
            "engine-loopback",
            "engine-dht-state",
            "engine-ports",
            "network-ssl-certificate",
            "network-vpn-binding",
            "scheduler-queue",
            "scheduler-history",
            "scheduler-intervals",
            "scheduler-watchfolder",
            "scheduler-rss",
            "scheduler-cleanup",
            "scheduler-backup",
            "event-bus",
            "command-queue",
            "config-matrix",
            "categories-tags",
            "disk-appdata",
            "disk-download-paths",
            "disk-io",
        };

        foreach (var testId in injectedIds)
        {
            var result = await runner.RunTestAsync(testId);
            Assert.That(result.Status, Is.EqualTo("Passed"), $"{testId}: {result.ErrorMessage}");
            Assert.That(result.Output, Is.Not.Null.And.Not.Empty, testId);
        }
    }

    private static IMainDatabase CreateSqliteMainDatabaseMock()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        var connection = Substitute.For<IDbConnection>();
        mainDatabase.OpenConnection().Returns(connection);
        mainDatabase.DatabaseType.Returns(DatabaseType.SQLite);
        mainDatabase.Version.Returns(new Version(101, 0));

        connection.CreateCommand().Returns(_ => CreateDbCommandMock(DatabaseType.SQLite));
        return mainDatabase;
    }

    private static IMainDatabase CreatePostgreSqlMainDatabaseMock()
    {
        var mainDatabase = Substitute.For<IMainDatabase>();
        var connection = Substitute.For<IDbConnection>();
        mainDatabase.OpenConnection().Returns(connection);
        mainDatabase.DatabaseType.Returns(DatabaseType.PostgreSQL);
        mainDatabase.Version.Returns(new Version(16, 0));

        connection.CreateCommand().Returns(_ => CreateDbCommandMock(DatabaseType.PostgreSQL));
        return mainDatabase;
    }

    private static IDbCommand CreateDbCommandMock(DatabaseType databaseType)
    {
        var cmd = Substitute.For<IDbCommand>();

        cmd.ExecuteScalar().Returns(callInfo =>
        {
            var text = cmd.CommandText ?? string.Empty;
            if (text.Contains("quick_check", StringComparison.OrdinalIgnoreCase))
            {
                return "ok";
            }

            if (text.Contains("sqlite_sequence", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (text.Contains("sqlite_master", StringComparison.OrdinalIgnoreCase) && text.Contains("index", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            if (text.Contains("COUNT", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (text.Equals("SELECT 1;", StringComparison.Ordinal))
            {
                return 1;
            }

            return 0;
        });

        cmd.ExecuteReader().Returns(callInfo =>
        {
            var text = cmd.CommandText ?? string.Empty;
            var reader = Substitute.For<IDataReader>();

            if (databaseType == DatabaseType.SQLite && text.Contains("foreign_key_check", StringComparison.OrdinalIgnoreCase))
            {
                reader.Read().Returns(false);
                return reader;
            }

            if (databaseType == DatabaseType.SQLite && text.Contains("wal_checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                reader.Read().Returns(true, false);
                reader.GetInt32(0).Returns(0);
                reader.GetInt32(1).Returns(12);
                reader.GetInt32(2).Returns(6);
                return reader;
            }

            if (databaseType == DatabaseType.SQLite && text.Contains("page_size", StringComparison.OrdinalIgnoreCase))
            {
                reader.Read().Returns(true, false);
                reader.GetInt32(0).Returns(4096);
                return reader;
            }

            reader.Read().Returns(false);
            return reader;
        });

        return cmd;
    }
}
