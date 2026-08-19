using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(18)]
public class AddPeerConnectionLogs : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("PeerConnectionLogs")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("InfoHash").AsString().NotNullable()
            .WithColumn("TorrentName").AsString().Nullable()
            .WithColumn("RemoteIp").AsString().NotNullable()
            .WithColumn("RemotePort").AsInt32().NotNullable()
            .WithColumn("PeerId").AsString().Nullable()
            .WithColumn("IsEncrypted").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("EventType").AsString().NotNullable()
            .WithColumn("Timestamp").AsDateTime().NotNullable();

        Create.Index("IX_PeerConnectionLogs_Timestamp")
            .OnTable("PeerConnectionLogs")
            .OnColumn("Timestamp");

        Create.Index("IX_PeerConnectionLogs_InfoHash")
            .OnTable("PeerConnectionLogs")
            .OnColumn("InfoHash");
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }
}
