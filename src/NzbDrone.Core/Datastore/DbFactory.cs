using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Npgsql;

namespace NzbDrone.Core.Datastore;

public interface IDbFactory
{
    IDatabase Create(DatabaseType dbType, string connectionString);
}

public class SqliteDoubleTypeHandler : SqlMapper.TypeHandler<double>
{
    public override void SetValue(IDbDataParameter parameter, double value)
    {
        parameter.Value = value;
    }

    public override double Parse(object value)
    {
        return Convert.ToDouble(value);
    }
}

public class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        parameter.Value = value.ToString("HH:mm:ss");
    }

    public override TimeOnly Parse(object value)
    {
        return TimeOnly.Parse((string)value);
    }
}

public class DbFactory : IDbFactory
{
    private static bool _typeHandlersRegistered;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public IDatabase Create(DatabaseType dbType, string connectionString)
    {
        if (!_typeHandlersRegistered)
        {
            SqlMapper.AddTypeHandler(new SqliteDoubleTypeHandler());
            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<int>>());
            _typeHandlersRegistered = true;
        }

        _logger.Info("Creating {0} database: {1}", dbType, RedactConnectionString(dbType, connectionString));

        var sqliteConnectionString = dbType == DatabaseType.SQLite ? CleanSqliteConnectionString(connectionString) : connectionString;

        RunMigrations(dbType, sqliteConnectionString);

        if (dbType == DatabaseType.SQLite)
        {
            EnableSqlitePragmas(sqliteConnectionString);
        }

        Func<IDbConnection> factory = dbType switch
        {
            DatabaseType.PostgreSQL => () => new NpgsqlConnection(connectionString),
            _ => () =>
            {
                var conn = new SqliteConnection(sqliteConnectionString);
                conn.StateChange += (sender, args) =>
                {
                    if (args.CurrentState == ConnectionState.Open && sender is SqliteConnection sqliteConn)
                    {
                        using var cmd = sqliteConn.CreateCommand();
                        cmd.CommandText = "PRAGMA busy_timeout=30000;";
                        cmd.ExecuteNonQuery();
                    }
                };
                return conn;
            }
        };

        return new Database(factory, dbType);
    }

    public static string CleanSqliteConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return System.Text.RegularExpressions.Regex.Replace(connectionString, @"(?i)Busy\s+Timeout\s*=\s*[^;]+;?", "");
    }

    private void EnableSqlitePragmas(string connectionString)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to apply SQLite pragmas");
        }
    }

    private static string RedactConnectionString(DatabaseType dbType, string connectionString)
    {
        if (dbType == DatabaseType.PostgreSQL)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return $"Host={builder.Host};Database={builder.Database}";
        }

        var sqliteBuilder = new SqliteConnectionStringBuilder(CleanSqliteConnectionString(connectionString));
        return $"Data Source={sqliteBuilder.DataSource}";
    }

    private void RunMigrations(DatabaseType dbType, string connectionString)
    {
        var services = new ServiceCollection();

        services.AddFluentMigratorCore()
            .ConfigureRunner(rb =>
            {
                if (dbType == DatabaseType.PostgreSQL)
                {
                    rb.AddPostgres();
                }
                else
                {
                    rb.AddSQLite();
                }

                rb.WithGlobalConnectionString(connectionString)
                    .ScanIn(Assembly.GetExecutingAssembly()).For.Migrations();
            })
            .AddLogging(lb => lb.AddFluentMigratorConsole());

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        _logger.Info("Database migrations complete");
    }
}
