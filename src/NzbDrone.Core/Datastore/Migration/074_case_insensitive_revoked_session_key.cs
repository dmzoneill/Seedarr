using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(74)]
public class CaseInsensitiveRevokedSessionKey : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("RevokedSessions").Exists())
        {
            return;
        }

        Execute.Sql(
            """
            DELETE FROM "RevokedSessions"
            WHERE "Id" NOT IN (
                SELECT MIN("Id")
                FROM "RevokedSessions"
                GROUP BY LOWER(TRIM("SessionKey"))
            );
            """);

        Execute.Sql("UPDATE \"RevokedSessions\" SET \"SessionKey\" = LOWER(TRIM(\"SessionKey\")) WHERE \"SessionKey\" IS NOT NULL;");

        if (Schema.Table("RevokedSessions").Index("IX_RevokedSessions_SessionKey").Exists())
        {
            Delete.Index("IX_RevokedSessions_SessionKey").OnTable("RevokedSessions");
        }
        else
        {
            Execute.Sql("DROP INDEX IF EXISTS \"IX_RevokedSessions_SessionKey\";");
        }

        Create.Index("IX_RevokedSessions_SessionKey")
            .OnTable("RevokedSessions")
            .OnColumn("SessionKey").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        if (!Schema.Table("RevokedSessions").Exists())
        {
            return;
        }

        Execute.Sql("DROP INDEX IF EXISTS \"IX_RevokedSessions_SessionKey\";");
        Create.Index("IX_RevokedSessions_SessionKey")
            .OnTable("RevokedSessions")
            .OnColumn("SessionKey")
            .Unique();
    }
}
