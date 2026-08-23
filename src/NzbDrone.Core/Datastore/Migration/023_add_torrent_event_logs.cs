using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(23)]
public class AddTorrentEventLogs : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("TorrentEventLogs")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TorrentId").AsInt32().NotNullable()
            .WithColumn("TimeStamp").AsDateTime().NotNullable()
            .WithColumn("Level").AsString().NotNullable()
            .WithColumn("Source").AsString().Nullable()
            .WithColumn("Message").AsString().NotNullable();

        Create.Index("IX_TorrentEventLogs_TorrentId")
            .OnTable("TorrentEventLogs")
            .OnColumn("TorrentId");

        Create.Index("IX_TorrentEventLogs_TimeStamp")
            .OnTable("TorrentEventLogs")
            .OnColumn("TimeStamp");
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }
}
