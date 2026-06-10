using System;
using System.Data;

namespace NzbDrone.Core.Datastore;

public interface IMainDatabase : IDatabase
{
}

public class MainDatabase : IMainDatabase
{
    private readonly IDatabase _database;

    public MainDatabase(IDbFactory dbFactory, IConnectionStringFactory connectionStringFactory)
    {
        _database = dbFactory.Create(
            connectionStringFactory.DatabaseType,
            connectionStringFactory.MainDbConnectionString);
    }

    public IDbConnection OpenConnection() => _database.OpenConnection();
    public DatabaseType DatabaseType => _database.DatabaseType;
    public Version Version => _database.Version;
}
