using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(72)]
public class AddTorrentInfoHashV2 : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("Torrents").Exists())
        {
            return;
        }

        if (!Schema.Table("Torrents").Column("InfoHashV2").Exists())
        {
            Alter.Table("Torrents")
                .AddColumn("InfoHashV2").AsString().Nullable();
        }

        Execute.Sql("UPDATE \"Torrents\" SET \"InfoHashV2\" = LOWER(TRIM(\"InfoHashV2\")) WHERE \"InfoHashV2\" IS NOT NULL;");

        Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Torrents_InfoHashV2\" ON \"Torrents\" (LOWER(\"InfoHashV2\")) WHERE \"InfoHashV2\" IS NOT NULL;");
    }

    public override void Down()
    {
        if (!Schema.Table("Torrents").Exists())
        {
            return;
        }

        Execute.Sql("DROP INDEX IF EXISTS \"IX_Torrents_InfoHashV2\";");

        if (Schema.Table("Torrents").Column("InfoHashV2").Exists())
        {
            Delete.Column("InfoHashV2").FromTable("Torrents");
        }
    }
}
