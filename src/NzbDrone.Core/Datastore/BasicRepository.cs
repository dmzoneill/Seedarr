using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using Npgsql;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Datastore;

public interface IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    IEnumerable<TModel> All();
    TModel Get(int id);
    TModel Insert(TModel model);
    void InsertMany(IList<TModel> models);
    void InsertMany(IEnumerable<TModel> models);
    TModel Update(TModel model);
    void UpdateMany(IEnumerable<TModel> models);
    void Delete(int id);
    void Delete(TModel model);
}

public class BasicRepository<TModel> : IBasicRepository<TModel>
    where TModel : ModelBase, new()
{
    protected internal static readonly RetryPolicy RetryPolicy = Policy
        .Handle<SqliteException>(ex => ex.SqliteErrorCode is 5 or 6)
        .Or<PostgresException>(ex => ex.SqlState is "40001" or "40P01" or "08000" or "08006" || ex.IsTransient)
        .Or<NpgsqlException>(ex => ex.IsTransient)
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

    public IDatabase Database => _database;

    public BasicRepository(IDatabase database)
    {
        _database = database;
        _table = TableMapping.GetTableName(typeof(TModel));
    }

    protected TResult QueryWithRetry<TResult>(Func<IDbConnection, TResult> query)
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            return query(connection);
        });
    }

    protected void ExecuteWithRetry(Action<IDbConnection> action)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            action(connection);
        });
    }

    public IEnumerable<TModel> All()
    {
        return QueryWithRetry(connection =>
            connection.Query<TModel>($"SELECT * FROM \"{_table}\""));
    }

    public TModel Get(int id)
    {
        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<TModel>(
                $"SELECT * FROM \"{_table}\" WHERE \"Id\" = @Id",
                new { Id = id }));
    }

    public TModel Insert(TModel model)
    {
        return QueryWithRetry(connection =>
        {
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

    public void InsertMany(IList<TModel> models)
    {
        if (models == null || models.Count == 0)
        {
            return;
        }

        ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                connection.Execute(
                    TableMapping.GetInsertSql<TModel>(_table),
                    models,
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

    public void InsertMany(IEnumerable<TModel> models)
    {
        if (models == null)
        {
            return;
        }

        var list = models as IList<TModel> ?? models.ToList();
        InsertMany(list);
    }

    public TModel Update(TModel model)
    {
        return QueryWithRetry(connection =>
        {
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

        ExecuteWithRetry(connection =>
        {
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
        ExecuteWithRetry(connection =>
        {
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
