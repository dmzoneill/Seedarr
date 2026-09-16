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
        IDatabase database = null)
    {
        _repo = repo;
        _eventAggregator = eventAggregator;
        _torrentRepository = torrentRepository;
        _notificationRepository = notificationRepository;
        _indexerRepository = indexerRepository;
        _downloadClientRepository = downloadClientRepository;
        _arrConnectionRepository = arrConnectionRepository;
        _automationScriptRepository = automationScriptRepository;
        _database = database ?? (repo as BasicRepository<Tag>)?.Database;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<Tag> GetAll() => _repo.All().ToList();
    public Tag Get(int id) => _repo.Get(id);

    public Tag Add(Tag tag)
    {
        _logger.Info("Adding tag: {0}", tag.Label);
        var result = _repo.Insert(tag);
        _eventAggregator.PublishEvent(new ModelEvent<Tag>(result, ModelAction.Created));
        return result;
    }

    public Tag Update(Tag tag)
    {
        _logger.Info("Updating tag: {0}", tag.Label);
        var result = _repo.Update(tag);
        _eventAggregator.PublishEvent(new ModelEvent<Tag>(result, ModelAction.Updated));
        return result;
    }

    public void Delete(int id)
    {
        var tag = _repo.Get(id);
        _logger.Info("Deleting tag: {0}", id);

        if (_database != null)
        {
            DeleteCascadingTransactional(id);
        }
        else
        {
            DeleteCascadingRepositories(id);
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

    private void DeleteCascadingTransactional(int id)
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

    private void DeleteCascadingRepositories(int id)
    {
        if (_torrentRepository != null)
        {
            var torrents = _torrentRepository.All().Where(t => t.TagIds != null && t.TagIds.Contains(id)).ToList();
            if (torrents.Count > 0)
            {
                foreach (var torrent in torrents)
                {
                    torrent.TagIds.RemoveAll(t => t == id);
                }

                _torrentRepository.UpdateMany(torrents);
            }
        }

        if (_notificationRepository != null)
        {
            var notifications = _notificationRepository.All().Where(n => n.Tags != null && n.Tags.Contains(id)).ToList();
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
            var indexers = _indexerRepository.All().Where(i => i.Tags != null && i.Tags.Contains(id)).ToList();
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
            var clients = _downloadClientRepository.All().Where(c => c.Tags != null && c.Tags.Contains(id)).ToList();
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
            var arrs = _arrConnectionRepository.All().Where(a => a.Tags != null && a.Tags.Contains(id)).ToList();
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
            var scripts = _automationScriptRepository.All().Where(s => s.TargetTagIds != null && s.TargetTagIds.Contains(id)).ToList();
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
