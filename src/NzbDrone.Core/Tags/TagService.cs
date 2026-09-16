using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using NLog;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Torrents;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Tags;

public class TagService : ITagService
{
    private static readonly RetryPolicy RetryPolicy = Policy
        .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
        .WaitAndRetry(new[]
        {
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1000)
        });

    private static readonly (string Table, string Column)[] TagTargets =
    {
        ("Torrents", "TagIds"),
        ("NotificationDefinitions", "Tags"),
        ("IndexerDefinitions", "Tags"),
        ("DownloadClientDefinitions", "Tags"),
        ("ArrConnectionDefinitions", "Tags"),
        ("AutomationScripts", "TargetTagIds")
    };

    private readonly ITagRepository _repo;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentRepository _torrentRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IIndexerRepository _indexerRepository;
    private readonly IDownloadClientRepository _downloadClientRepository;
    private readonly IArrConnectionRepository _arrConnectionRepository;
    private readonly IAutomationScriptRepository _automationScriptRepository;
    private readonly ITorrentService _torrentService;
    private readonly IDatabase _database;
    private readonly Logger _logger;

    public TagService(
        ITagRepository repo,
        IEventAggregator eventAggregator,
        ITorrentRepository torrentRepository = null,
        INotificationRepository notificationRepository = null,
        IIndexerRepository indexerRepository = null,
        IDownloadClientRepository downloadClientRepository = null,
        IArrConnectionRepository arrConnectionRepository = null,
        IAutomationScriptRepository automationScriptRepository = null,
        IDatabase database = null,
        ITorrentService torrentService = null,
        IMainDatabase mainDatabase = null)
    {
        _repo = repo;
        _eventAggregator = eventAggregator;
        _torrentRepository = torrentRepository;
        _notificationRepository = notificationRepository;
        _indexerRepository = indexerRepository;
        _downloadClientRepository = downloadClientRepository;
        _arrConnectionRepository = arrConnectionRepository;
        _automationScriptRepository = automationScriptRepository;
        _torrentService = torrentService;
        _database = mainDatabase
            ?? database
            ?? (repo as BasicRepository<Tag>)?.Database
            ?? (torrentRepository as BasicRepository<Torrent>)?.Database
            ?? (notificationRepository as BasicRepository<NotificationDefinition>)?.Database
            ?? (indexerRepository as BasicRepository<IndexerDefinition>)?.Database
            ?? (downloadClientRepository as BasicRepository<DownloadClientDefinition>)?.Database
            ?? (arrConnectionRepository as BasicRepository<ArrConnectionDefinition>)?.Database
            ?? (automationScriptRepository as BasicRepository<AutomationScript>)?.Database;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<Tag> GetAll() => _repo.All()?.ToList() ?? new List<Tag>();
    public Tag Get(int id) => _repo.Get(id);

    public Tag Add(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (string.IsNullOrWhiteSpace(tag.Label))
        {
            throw new ArgumentException("Tag label cannot be empty.", nameof(tag));
        }

        var trimmedLabel = tag.Label.Trim();
        var existingTags = GetAll();
        if (existingTags.Any(t => string.Equals(t.Label?.Trim(), trimmedLabel, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateTagException(trimmedLabel);
        }

        tag.Label = trimmedLabel;
        _logger.Info("Adding tag: {0}", tag.Label);
        var result = _repo.Insert(tag);
        _eventAggregator.PublishEvent(new ModelEvent<Tag>(result, ModelAction.Created));
        return result;
    }

    public Tag Update(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (string.IsNullOrWhiteSpace(tag.Label))
        {
            throw new ArgumentException("Tag label cannot be empty.", nameof(tag));
        }

        var trimmedLabel = tag.Label.Trim();
        var existingTags = GetAll();
        if (existingTags.Any(t => t.Id != tag.Id && string.Equals(t.Label?.Trim(), trimmedLabel, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateTagException(trimmedLabel);
        }

        tag.Label = trimmedLabel;
        _logger.Info("Updating tag: {0}", tag.Label);
        var result = _repo.Update(tag);
        _eventAggregator.PublishEvent(new ModelEvent<Tag>(result, ModelAction.Updated));
        return result;
    }

    public void Delete(int id)
    {
        var tag = _repo.Get(id);
        _logger.Info("Deleting tag: {0}", id);

        var tagLabel = tag?.Label;

        LogNullRepositoryWarnings();

        if (_database != null)
        {
            DeleteCascadingTransactional(id, tagLabel);
        }
        else
        {
            DeleteCascadingRepositories(id, tagLabel);
            _repo.Delete(id);
        }

        if (tag != null)
        {
            _eventAggregator.PublishEvent(new ModelEvent<Tag>(tag, ModelAction.Deleted));
        }
    }

    public List<int> SyncTagsFromLabels(IEnumerable<string> labels)
    {
        if (labels == null)
        {
            return new List<int>();
        }

        var candidateLabels = new List<string>();
        foreach (var raw in labels)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    candidateLabels.Add(trimmed);
                }
            }
        }

        if (candidateLabels.Count == 0)
        {
            return new List<int>();
        }

        var allTags = GetAll();
        var tagMap = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in allTags)
        {
            if (!string.IsNullOrWhiteSpace(tag.Label) && !tagMap.ContainsKey(tag.Label))
            {
                tagMap[tag.Label] = tag;
            }
        }

        var resultIds = new List<int>();
        foreach (var label in candidateLabels)
        {
            if (!tagMap.TryGetValue(label, out var tag))
            {
                tag = Add(new Tag { Label = label });
                tagMap[label] = tag;
            }

            if (!resultIds.Contains(tag.Id))
            {
                resultIds.Add(tag.Id);
            }
        }

        return resultIds;
    }

    public List<string> GetLabelsForTagIds(IEnumerable<int> tagIds)
    {
        if (tagIds == null)
        {
            return new List<string>();
        }

        var idList = tagIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new List<string>();
        }

        var allTags = GetAll().ToDictionary(t => t.Id);
        var labels = new List<string>();

        foreach (var id in idList)
        {
            if (allTags.TryGetValue(id, out var tag) && !string.IsNullOrWhiteSpace(tag.Label))
            {
                labels.Add(tag.Label);
            }
        }

        return labels;
    }

    private void LogNullRepositoryWarnings()
    {
        if (_torrentRepository == null && _torrentService == null)
        {
            _logger.Warn("Torrent repository and torrent service are null; cascading tag deletion for torrents cannot be executed via repository.");
        }

        if (_notificationRepository == null)
        {
            _logger.Warn("Notification repository is null; cascading tag deletion for notifications cannot be executed via repository.");
        }

        if (_indexerRepository == null)
        {
            _logger.Warn("Indexer repository is null; cascading tag deletion for indexers cannot be executed via repository.");
        }

        if (_downloadClientRepository == null)
        {
            _logger.Warn("Download client repository is null; cascading tag deletion for download clients cannot be executed via repository.");
        }

        if (_arrConnectionRepository == null)
        {
            _logger.Warn("Arr connection repository is null; cascading tag deletion for arr connections cannot be executed via repository.");
        }

        if (_automationScriptRepository == null)
        {
            _logger.Warn("Automation script repository is null; cascading tag deletion for automation scripts cannot be executed via repository.");
        }
    }

    private void DeleteCascadingTransactional(int id, string tagLabel)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                var dbType = _database.DatabaseType;

                foreach (var (table, column) in TagTargets)
                {
                    if (!TableExists(connection, transaction, table, dbType))
                    {
                        continue;
                    }

                    if (dbType == DatabaseType.SQLite)
                    {
                        var sql = $@"
                            UPDATE ""{table}""
                            SET ""{column}"" = COALESCE((
                                SELECT json_group_array(value)
                                FROM json_each(""{table}"".""{column}"")
                                WHERE value != @TagId
                            ), '[]')
                            WHERE ""{column}"" IS NOT NULL
                              AND ""{column}"" != ''
                              AND ""{column}"" != '[]'
                              AND json_valid(""{column}"")
                              AND EXISTS (
                                  SELECT 1
                                  FROM json_each(""{table}"".""{column}"")
                                  WHERE value = @TagId
                              )";

                        connection.Execute(sql, new { TagId = id }, transaction);
                    }
                    else if (dbType == DatabaseType.PostgreSQL)
                    {
                        var sql = $@"
                            UPDATE ""{table}""
                            SET ""{column}"" = COALESCE((
                                SELECT json_agg(elem::int)::text
                                FROM json_array_elements_text(""{column}""::json) AS elem
                                WHERE elem::int != @TagId
                            ), '[]')
                            WHERE ""{column}"" IS NOT NULL
                              AND ""{column}"" != ''
                              AND ""{column}"" != '[]'
                              AND EXISTS (
                                  SELECT 1
                                  FROM json_array_elements_text(""{column}""::json) AS elem
                                  WHERE elem::int = @TagId
                              )";

                        connection.Execute(sql, new { TagId = id }, transaction);
                    }
                }

                if (!string.IsNullOrWhiteSpace(tagLabel) &&
                    TableExists(connection, transaction, "Torrents", dbType) &&
                    ColumnExists(connection, transaction, "Torrents", "Label", dbType))
                {
                    ScrubTorrentLabelsTransactional(connection, transaction, tagLabel);
                }

                connection.Execute(
                    "DELETE FROM \"Tags\" WHERE \"Id\" = @TagId",
                    new { TagId = id },
                    transaction);

                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // best-effort rollback
                }

                throw;
            }
        });
    }

    private static void ScrubTorrentLabelsTransactional(IDbConnection connection, IDbTransaction transaction, string tagLabel)
    {
        var torrents = connection.Query<(int Id, string Label)>(
            "SELECT \"Id\", \"Label\" FROM \"Torrents\" WHERE \"Label\" IS NOT NULL AND \"Label\" != ''",
            transaction: transaction).ToList();

        foreach (var (torrentId, label) in torrents)
        {
            if (ContainsTagLabel(label, tagLabel))
            {
                var newLabel = ScrubLabel(label, tagLabel);
                connection.Execute(
                    "UPDATE \"Torrents\" SET \"Label\" = @NewLabel WHERE \"Id\" = @Id",
                    new { NewLabel = newLabel, Id = torrentId },
                    transaction);
            }
        }
    }

    private static bool ColumnExists(IDbConnection connection, IDbTransaction transaction, string table, string column, DatabaseType dbType)
    {
        if (dbType == DatabaseType.SQLite)
        {
            return connection.ExecuteScalar<int>(
                $"SELECT COUNT(1) FROM pragma_table_info('{table}') WHERE name=@column",
                new { column },
                transaction) > 0;
        }
        else
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM information_schema.columns WHERE (table_name=@table OR table_name=LOWER(@table)) AND (column_name=@column OR column_name=LOWER(@column))",
                new { table, column },
                transaction) > 0;
        }
    }

    private static bool TableExists(IDbConnection connection, IDbTransaction transaction, string table, DatabaseType dbType)
    {
        if (dbType == DatabaseType.SQLite)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@table",
                new { table },
                transaction) > 0;
        }
        else
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM information_schema.tables WHERE table_name=@table OR table_name=LOWER(@table)",
                new { table },
                transaction) > 0;
        }
    }

    private static string ScrubLabel(string currentLabel, string tagLabel)
    {
        if (string.IsNullOrWhiteSpace(currentLabel) || string.IsNullOrWhiteSpace(tagLabel))
        {
            return currentLabel ?? string.Empty;
        }

        var parts = currentLabel.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p) && !string.Equals(p, tagLabel.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
    }

    private static bool ContainsTagLabel(string currentLabel, string tagLabel)
    {
        if (string.IsNullOrWhiteSpace(currentLabel) || string.IsNullOrWhiteSpace(tagLabel))
        {
            return false;
        }

        var parts = currentLabel.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim());

        return parts.Any(p => string.Equals(p, tagLabel.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void DeleteCascadingRepositories(int id, string tagLabel)
    {
        if (_torrentService != null)
        {
            var torrents = _torrentService.GetAll()?
                .Where(t => (t.TagIds != null && t.TagIds.Contains(id)) ||
                            (!string.IsNullOrWhiteSpace(tagLabel) && ContainsTagLabel(t.Label, tagLabel)))
                .ToList() ?? new List<Torrent>();

            foreach (var torrent in torrents)
            {
                torrent.TagIds?.RemoveAll(t => t == id);
                if (!string.IsNullOrWhiteSpace(tagLabel))
                {
                    torrent.Label = ScrubLabel(torrent.Label, tagLabel);
                }

                _torrentService.UpdateUserFields(torrent);
            }
        }
        else if (_torrentRepository != null)
        {
            var torrents = _torrentRepository.All()?
                .Where(t => (t.TagIds != null && t.TagIds.Contains(id)) ||
                            (!string.IsNullOrWhiteSpace(tagLabel) && ContainsTagLabel(t.Label, tagLabel)))
                .ToList() ?? new List<Torrent>();

            if (torrents.Count > 0)
            {
                foreach (var torrent in torrents)
                {
                    torrent.TagIds?.RemoveAll(t => t == id);
                    if (!string.IsNullOrWhiteSpace(tagLabel))
                    {
                        torrent.Label = ScrubLabel(torrent.Label, tagLabel);
                    }
                }

                _torrentRepository.UpdateTagsAndLabels(torrents);
            }
        }

        if (_notificationRepository != null)
        {
            var notifications = _notificationRepository.All()?.Where(n => n.Tags != null && n.Tags.Contains(id)).ToList() ?? new List<NotificationDefinition>();
            if (notifications.Count > 0)
            {
                foreach (var notif in notifications)
                {
                    notif.Tags.RemoveAll(t => t == id);
                }

                _notificationRepository.UpdateMany(notifications);
            }
        }

        if (_indexerRepository != null)
        {
            var indexers = _indexerRepository.All()?.Where(i => i.Tags != null && i.Tags.Contains(id)).ToList() ?? new List<IndexerDefinition>();
            if (indexers.Count > 0)
            {
                foreach (var indexer in indexers)
                {
                    indexer.Tags.RemoveAll(t => t == id);
                }

                _indexerRepository.UpdateMany(indexers);
            }
        }

        if (_downloadClientRepository != null)
        {
            var clients = _downloadClientRepository.All()?.Where(c => c.Tags != null && c.Tags.Contains(id)).ToList() ?? new List<DownloadClientDefinition>();
            if (clients.Count > 0)
            {
                foreach (var client in clients)
                {
                    client.Tags.RemoveAll(t => t == id);
                }

                _downloadClientRepository.UpdateMany(clients);
            }
        }

        if (_arrConnectionRepository != null)
        {
            var arrs = _arrConnectionRepository.All()?.Where(a => a.Tags != null && a.Tags.Contains(id)).ToList() ?? new List<ArrConnectionDefinition>();
            if (arrs.Count > 0)
            {
                foreach (var arr in arrs)
                {
                    arr.Tags.RemoveAll(t => t == id);
                }

                _arrConnectionRepository.UpdateMany(arrs);
            }
        }

        if (_automationScriptRepository != null)
        {
            var scripts = _automationScriptRepository.All()?.Where(s => s.TargetTagIds != null && s.TargetTagIds.Contains(id)).ToList() ?? new List<AutomationScript>();
            if (scripts.Count > 0)
            {
                foreach (var script in scripts)
                {
                    script.TargetTagIds.RemoveAll(t => t == id);
                }

                _automationScriptRepository.UpdateMany(scripts);
            }
        }
    }
}
