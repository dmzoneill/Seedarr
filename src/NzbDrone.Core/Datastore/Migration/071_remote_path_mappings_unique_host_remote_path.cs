using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(71)]
public class RemotePathMappingsUniqueHostRemotePath : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("RemotePathMappings").Exists())
        {
            return;
        }

        Execute.Sql(
            """
            DELETE FROM "RemotePathMappings"
            WHERE "Id" NOT IN (
                SELECT MIN("Id")
                FROM "RemotePathMappings"
                GROUP BY LOWER(TRIM("Host")), TRIM("RemotePath")
            );
            """);

        if (!Schema.Table("RemotePathMappings").Index("IX_RemotePathMappings_Host_RemotePath").Exists())
        {
            Execute.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_RemotePathMappings_Host_RemotePath"
                ON "RemotePathMappings" (LOWER(TRIM("Host")), TRIM("RemotePath"));
                """);
        }
    }

    public override void Down()
    {
        if (Schema.Table("RemotePathMappings").Exists() &&
            Schema.Table("RemotePathMappings").Index("IX_RemotePathMappings_Host_RemotePath").Exists())
        {
            Delete.Index("IX_RemotePathMappings_Host_RemotePath").OnTable("RemotePathMappings");
        }
    }
}
