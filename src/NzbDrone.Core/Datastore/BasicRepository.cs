using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Datastore;

public interface IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    IEnumerable<TModel> All();
    TModel Get(int id);
    TModel Insert(TModel model);
    TModel Update(TModel model);
    void UpdateMany(IEnumerable<TModel> models);
    void Delete(int id);
    void Delete(TModel model);
}

public class BasicRepository<TModel> : IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    protected static readonly RetryPolicy RetryPolicy = Policy
        .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
        .WaitAndRetry(new[]
        {
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1000)
        });

    private readonly IDatabase _database;
    protected readonly string _table;

    public BasicRepository(IDatabase database)
    {
        _database = database;
        _table = TableMapping.GetTableName(typeof(TModel));
    }

    public IEnumerable<TModel> All()
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            return connection.Query<TModel>($"SELECT * FROM \"{_table}\"");
        });
    }

    public TModel Get(int id)
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            return connection.QueryFirstOrDefault<TModel>(
                $"SELECT * FROM \"{_table}\" WHERE \"Id\" = @Id",
                new { Id = id });
        });
    }

    public TModel Insert(TModel model)
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();

            if (_database.DatabaseType == DatabaseType.SQLite)
            {
                var id = connection.ExecuteScalar<int>(
                    TableMapping.GetInsertSql(_table, model) + "; SELECT last_insert_rowid()",
                    model);
                model.Id = id;
            }
            else
            {
                var id = connection.ExecuteScalar<int>(
                    TableMapping.GetInsertSql(_table, model) + " RETURNING \"Id\"",
                    model);
                model.Id = id;
            }

            return model;
        });
    }

    public TModel Update(TModel model)
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            connection.Execute(
                TableMapping.GetUpdateSql<TModel>(_table, model),
                model);
            return model;
        });
    }

    public void UpdateMany(IEnumerable<TModel> models)
    {
        var list = models as IList<TModel> ?? models.ToList();
        if (list.Count == 0)
        {
            return;
        }

        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                connection.Execute(
                    TableMapping.GetUpdateSql<TModel>(_table),
                    list,
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

    public virtual void Delete(int id)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            connection.Execute(
                $"DELETE FROM \"{_table}\" WHERE \"Id\" = @Id",
                new { Id = id });
        });
    }

    public void Delete(TModel model)
    {
        Delete(model.Id);
    }
}
