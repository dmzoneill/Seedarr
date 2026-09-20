using System;
using NLog;

namespace NzbDrone.Core.Datastore;

public class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly IMainDatabase _mainDatabase;
    private readonly Logger _logger;

    public DatabaseMaintenanceService(IMainDatabase mainDatabase)
    {
        _mainDatabase = mainDatabase;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public long GetFreelistCount()
    {
        return _mainDatabase?.GetFreelistCount() ?? 0;
    }

    public long GetPageSize()
    {
        return _mainDatabase?.GetPageSize() ?? 4096;
    }

    public long GetFreelistBytes()
    {
        return GetFreelistCount() * GetPageSize();
    }

    public bool IsIncrementalAutoVacuumEnabled()
    {
        if (_mainDatabase == null || _mainDatabase.DatabaseType != DatabaseType.SQLite)
        {
            return true;
        }

        return _mainDatabase.GetAutoVacuumStatus() == 2;
    }

    public DatabaseMaintenanceResult Vacuum(int? maxPages = null)
    {
        return PerformMaintenance(maxPages);
    }

    public DatabaseMaintenanceResult PerformMaintenance(int? maxPages = null)
    {
        var dbTypeStr = _mainDatabase?.DatabaseType.ToString() ?? "Unknown";

        if (_mainDatabase == null)
        {
            return new DatabaseMaintenanceResult
            {
                Success = false,
                DatabaseType = dbTypeStr,
                Message = "Database is unavailable."
            };
        }

        if (_mainDatabase.DatabaseType != DatabaseType.SQLite)
        {
            try
            {
                _mainDatabase.Optimize();
                _mainDatabase.Vacuum();
                return new DatabaseMaintenanceResult
                {
                    Success = true,
                    DatabaseType = dbTypeStr,
                    InitialFreelistPages = 0,
                    FinalFreelistPages = 0,
                    ReclaimedPages = 0,
                    PageSize = 0,
                    ReclaimedBytes = 0,
                    Message = $"{dbTypeStr} database maintenance completed."
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to perform {0} database maintenance", dbTypeStr);
                return new DatabaseMaintenanceResult
                {
                    Success = false,
                    DatabaseType = dbTypeStr,
                    Message = ex.Message
                };
            }
        }

        try
        {
            _mainDatabase.Optimize();

            var pageSize = _mainDatabase.GetPageSize();
            var initialFreelist = _mainDatabase.GetFreelistCount();
            var autoVacuum = _mainDatabase.GetAutoVacuumStatus();

            _logger.Info(
                "Starting SQLite database maintenance. Initial freelist: {0} pages, Page size: {1} bytes, Auto-vacuum mode: {2}",
                initialFreelist,
                pageSize,
                autoVacuum);

            var pagesToReclaim = maxPages.GetValueOrDefault(5000);

            if (initialFreelist == 0 && autoVacuum == 2)
            {
                _mainDatabase.Checkpoint();
                return new DatabaseMaintenanceResult
                {
                    Success = true,
                    DatabaseType = dbTypeStr,
                    InitialFreelistPages = 0,
                    FinalFreelistPages = 0,
                    ReclaimedPages = 0,
                    PageSize = pageSize,
                    ReclaimedBytes = 0,
                    Message = "No reclaimable freelist pages found."
                };
            }

            _mainDatabase.IncrementalVacuum(pagesToReclaim);
            _mainDatabase.Checkpoint();

            var finalFreelist = _mainDatabase.GetFreelistCount();
            var reclaimedPages = Math.Max(0, initialFreelist - finalFreelist);
            var reclaimedBytes = reclaimedPages * pageSize;

            var message = reclaimedPages > 0
                ? $"Reclaimed {reclaimedPages} pages ({reclaimedBytes:N0} bytes)."
                : "Incremental vacuum executed; freelist unchanged.";

            _logger.Info(
                "SQLite database maintenance finished. Reclaimed {0} pages ({1:N0} bytes). Final freelist: {2} pages",
                reclaimedPages,
                reclaimedBytes,
                finalFreelist);

            return new DatabaseMaintenanceResult
            {
                Success = true,
                DatabaseType = dbTypeStr,
                InitialFreelistPages = initialFreelist,
                FinalFreelistPages = finalFreelist,
                ReclaimedPages = reclaimedPages,
                PageSize = pageSize,
                ReclaimedBytes = reclaimedBytes,
                Message = message
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform SQLite database maintenance");
            return new DatabaseMaintenanceResult
            {
                Success = false,
                DatabaseType = dbTypeStr,
                Message = ex.Message
            };
        }
    }
}
