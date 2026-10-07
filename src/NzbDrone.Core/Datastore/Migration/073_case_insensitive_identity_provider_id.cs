using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(73)]
public class CaseInsensitiveIdentityProviderId : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("IdentityProviders").Exists())
        {
            return;
        }

        Execute.Sql(
            """
            DELETE FROM "IdentityProviders"
            WHERE "Id" NOT IN (
                SELECT MIN("Id")
                FROM "IdentityProviders"
                GROUP BY LOWER(TRIM("ProviderId"))
            );
            """);

        Execute.Sql("UPDATE \"IdentityProviders\" SET \"ProviderId\" = LOWER(TRIM(\"ProviderId\")) WHERE \"ProviderId\" IS NOT NULL;");

        if (Schema.Table("IdentityProviders").Index("IX_IdentityProviders_ProviderId").Exists())
        {
            Delete.Index("IX_IdentityProviders_ProviderId").OnTable("IdentityProviders");
        }
        else
        {
            Execute.Sql("DROP INDEX IF EXISTS \"IX_IdentityProviders_ProviderId\";");
        }

        Execute.Sql("DROP INDEX IF EXISTS \"UC_IdentityProviders_ProviderId\";");
        Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_IdentityProviders_ProviderId\" ON \"IdentityProviders\" (LOWER(\"ProviderId\"));");
    }

    public override void Down()
    {
        if (!Schema.Table("IdentityProviders").Exists())
        {
            return;
        }

        Execute.Sql("DROP INDEX IF EXISTS \"IX_IdentityProviders_ProviderId\";");
        Create.Index("IX_IdentityProviders_ProviderId")
            .OnTable("IdentityProviders")
            .OnColumn("ProviderId")
            .Unique();
    }
}
