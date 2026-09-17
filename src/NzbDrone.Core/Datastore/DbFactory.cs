using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
        TableRegistration.RegisterTables();

        if (!_typeHandlersRegistered)
        {
            SqlMapper.AddTypeHandler(new SqliteDoubleTypeHandler());
            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<int>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<string>>());
            _typeHandlersRegistered = true;
        }

        _logger.Info("Creating {0} database: {1}", dbType, RedactConnectionString(dbType, connectionString));

        var migrationConnectionString = GetMigrationConnectionString(dbType, connectionString);
        var sqliteConnectionString = dbType == DatabaseType.SQLite ? migrationConnectionString : connectionString;

        if (dbType == DatabaseType.SQLite)
        {
            EnableSqlitePragmas(sqliteConnectionString);
        }

        RunMigrations(dbType, migrationConnectionString);

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

    public static string GetMigrationConnectionString(DatabaseType dbType, string connectionString)
    {
        return dbType == DatabaseType.SQLite ? CleanSqliteConnectionString(connectionString) : connectionString;
    }

    public static string CleanSqliteConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var busyTimeoutMatch = Regex.Match(
            connectionString,
            @"(?i)Busy\s+Timeout\s*=\s*(\d+)");

        var defaultTimeoutMatch = Regex.Match(
            connectionString,
            @"(?i)Default\s+Timeout\s*=\s*(\d+)");

        // Remove unsupported "Busy Timeout" parameter to prevent Microsoft.Data.Sqlite keyword exceptions
        var cleaned = Regex.Replace(
            connectionString,
            @"(?i)Busy\s+Timeout\s*=\s*[^;]+;?",
            "").Trim();

        var timeoutSeconds = 30;
        if (defaultTimeoutMatch.Success && int.TryParse(defaultTimeoutMatch.Groups[1].Value, out var parsedDefaultTimeout))
        {
            timeoutSeconds = parsedDefaultTimeout;
        }
        else if (busyTimeoutMatch.Success && int.TryParse(busyTimeoutMatch.Groups[1].Value, out var parsedBusyTimeout))
        {
            timeoutSeconds = parsedBusyTimeout >= 1000 ? parsedBusyTimeout / 1000 : parsedBusyTimeout;
        }

        if (!Regex.IsMatch(cleaned, @"(?i)Default\s+Timeout\s*="))
        {
            var separator = cleaned.EndsWith(";") || string.IsNullOrEmpty(cleaned) ? "" : ";";
            cleaned = $"{cleaned}{separator}Default Timeout={timeoutSeconds};";
        }

        if (!Regex.IsMatch(cleaned, @"(?i)Foreign\s+Keys\s*="))
        {
            var separator = cleaned.EndsWith(";") || string.IsNullOrEmpty(cleaned) ? "" : ";";
            cleaned = $"{cleaned}{separator}Foreign Keys=True;";
        }

        return cleaned;
    }

    public static string GetSqliteDbPath(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var cleaned = CleanSqliteConnectionString(connectionString);
        var builder = new SqliteConnectionStringBuilder(cleaned);
        var dataSource = builder.DataSource;

        if (string.IsNullOrWhiteSpace(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFullPath(dataSource);
    }

    public string CreatePreMigrationSnapshot(string connectionString)
    {
        var dbPath = GetSqliteDbPath(connectionString);
        if (dbPath == null || !File.Exists(dbPath))
        {
            return null;
        }

        FlushSqliteWal(connectionString);

        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var snapshotPath = $"{dbPath}.pre-migration-{timestamp}.bak";
            if (File.Exists(snapshotPath))
            {
                var counter = 1;
                while (File.Exists($"{dbPath}.pre-migration-{timestamp}_{counter}.bak"))
                {
                    counter++;
                }

                snapshotPath = $"{dbPath}.pre-migration-{timestamp}_{counter}.bak";
            }

            File.Copy(dbPath, snapshotPath, overwrite: true);
            _logger.Info("Created pre-migration database snapshot at {0}", snapshotPath);

            PrunePreMigrationSnapshots(dbPath, 3, _logger);

            return snapshotPath;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to create pre-migration database snapshot for {0}", dbPath);
            return null;
        }
    }

    public static void PrunePreMigrationSnapshots(string dbPath, int maxToRetain = 3, Logger logger = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(dir))
            {
                dir = Directory.GetCurrentDirectory();
            }

            if (!Directory.Exists(dir))
            {
                return;
            }

            var fileName = Path.GetFileName(dbPath);
            var searchPattern = $"{fileName}.pre-migration-*.bak";
            var snapshots = Directory.GetFiles(dir, searchPattern)
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (snapshots.Count > maxToRetain)
            {
                foreach (var oldSnapshot in snapshots.Skip(maxToRetain))
                {
                    try
                    {
                        File.Delete(oldSnapshot);
                        logger?.Info("Pruned old pre-migration database snapshot: {0}", oldSnapshot);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn(ex, "Failed to prune old pre-migration snapshot: {0}", oldSnapshot);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Warn(ex, "Failed to prune pre-migration database snapshots for {0}", dbPath);
        }
    }

    private void FlushSqliteWal(string connectionString)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to flush SQLite WAL before pre-migration snapshot");
        }
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
        string snapshotPath = null;
        if (dbType == DatabaseType.SQLite)
        {
            snapshotPath = CreatePreMigrationSnapshot(connectionString);
        }

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

        if (dbType == DatabaseType.SQLite)
        {
            try
            {
                runner.Processor.Execute("PRAGMA busy_timeout = 30000; PRAGMA foreign_keys = ON;");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to apply SQLite pragmas to migration runner connection");
            }
        }

        try
        {
            runner.MigrateUp();
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(snapshotPath))
            {
                _logger.Error(ex, "Database migration failed. A pre-migration snapshot is available at: {0}. Restore this backup file to recover the database.", snapshotPath);
            }
            else
            {
                _logger.Error(ex, "Database migration failed");
            }

            throw;
        }

        _logger.Info("Database migrations complete");
    }
}
