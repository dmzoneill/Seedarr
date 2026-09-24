using System;
using Dapper;
using NLog;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Categories;

public class CategoryRepository : BasicRepository<Category>, ICategoryRepository
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    public CategoryRepository(IDatabase database)
        : base(database)
    {
    }

    public Category GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<Category>(
                $"SELECT * FROM \"{_table}\" WHERE LOWER(\"Name\") = LOWER(@Name)",
                new { Name = name.Trim() }));
    }

    public Category GetDefault()
    {
        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<Category>(
                $"SELECT * FROM \"{_table}\" WHERE \"IsDefault\" = @IsDefault",
                new { IsDefault = true }));
    }

    public void SetExclusiveDefault(int categoryId)
    {
        ExecuteWithRetry(connection =>
        {
            using var tx = connection.BeginTransaction();
            try
            {
                connection.Execute(
                    $"UPDATE \"{_table}\" SET \"IsDefault\" = @IsDefault WHERE \"Id\" != @Id",
                    new { IsDefault = false, Id = categoryId },
                    tx);
                connection.Execute(
                    $"UPDATE \"{_table}\" SET \"IsDefault\" = @IsDefault WHERE \"Id\" = @Id",
                    new { IsDefault = true, Id = categoryId },
                    tx);
                tx.Commit();
            }
            catch
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Transaction rollback failed in SetDefaultCategory");
                }

                throw;
            }
        });
    }
}
