using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(41)]
public class CaseInsensitiveInfoHashIndex : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Torrents").Exists())
        {
            Execute.Sql("UPDATE \"Torrents\" SET \"InfoHash\" = LOWER(TRIM(\"InfoHash\")) WHERE \"InfoHash\" IS NOT NULL;");

            if (Schema.Table("Torrents").Index("IX_Torrents_InfoHash").Exists())
            {
                Delete.Index("IX_Torrents_InfoHash").OnTable("Torrents");
            }
            else
            {
                Execute.Sql("DROP INDEX IF EXISTS \"IX_Torrents_InfoHash\";");
            }

            Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Torrents_InfoHash\" ON \"Torrents\" (\"InfoHash\" COLLATE NOCASE);");
        }
    }

    public override void Down()
    {
        if (Schema.Table("Torrents").Exists())
        {
            Execute.Sql("DROP INDEX IF EXISTS \"IX_Torrents_InfoHash\";");
            Create.Index("IX_Torrents_InfoHash")
                .OnTable("Torrents")
                .OnColumn("InfoHash");
        }
    }
}
