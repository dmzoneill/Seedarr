using System;
using System.Data;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DatabaseIntegrityCheck : IProvideHealthCheck
{
    private readonly IDatabase _database;
    private readonly Logger _logger;

    public DatabaseIntegrityCheck(IDatabase database = null)
    {
        _database = database;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public HealthCheckResult Check()
    {
        if (_database == null)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                "Database is unavailable or not configured.");
        }

        try
        {
            using var conn = _database.OpenConnection();
            if (conn == null)
            {
                return new HealthCheckResult(
                    GetType(),
                    HealthCheckResultType.Error,
                    "Database connection could not be opened.");
            }

            using var cmd = conn.CreateCommand();

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                cmd.CommandText = "SELECT 1;";
                cmd.ExecuteScalar();
                return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
            }

            cmd.CommandText = "PRAGMA quick_check;";
            var result = cmd.ExecuteScalar()?.ToString()?.Trim();

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
            }

            var detail = string.IsNullOrWhiteSpace(result)
                ? "Integrity check returned no results"
                : result;

            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"Database integrity check failed: {detail}");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Database integrity health check failed");
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"Database connectivity or integrity check failed: {ex.Message}");
        }
    }
}
