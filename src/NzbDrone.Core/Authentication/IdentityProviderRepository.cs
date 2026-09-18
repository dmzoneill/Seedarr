using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class IdentityProviderRepository : BasicRepository<IdentityProviderDefinition>, IIdentityProviderRepository
{
    public IdentityProviderRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<IdentityProviderDefinition> GetEnabled()
    {
        return QueryWithRetry(connection =>
            connection.Query<IdentityProviderDefinition>(
                $"SELECT * FROM \"{_table}\" WHERE \"IsEnabled\" = @IsEnabled ORDER BY \"Id\"",
                new { IsEnabled = true }));
    }

    public IdentityProviderDefinition FindByProviderId(string providerId)
    {
        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<IdentityProviderDefinition>(
                $"SELECT * FROM \"{_table}\" WHERE LOWER(\"ProviderId\") = LOWER(@ProviderId)",
                new { ProviderId = providerId }));
    }
}
