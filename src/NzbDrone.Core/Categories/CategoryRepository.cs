using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Categories;

public class CategoryRepository : BasicRepository<Category>, ICategoryRepository
{
    private readonly IDatabase _database;

    public CategoryRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public Category GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<Category>(
            $"SELECT * FROM \"{_table}\" WHERE LOWER(\"Name\") = LOWER(@Name)",
            new { Name = name.Trim() });
    }

    public Category GetDefault()
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<Category>(
            $"SELECT * FROM \"{_table}\" WHERE \"IsDefault\" = @IsDefault",
            new { IsDefault = true });
    }
}
