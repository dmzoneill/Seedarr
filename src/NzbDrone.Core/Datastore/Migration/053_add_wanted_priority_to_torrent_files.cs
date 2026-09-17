using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(53)]
public class AddWantedPriorityToTorrentFiles : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("TorrentFiles").Exists())
        {
            if (!Schema.Table("TorrentFiles").Column("Wanted").Exists())
            {
                Alter.Table("TorrentFiles")
                    .AddColumn("Wanted").AsBoolean().NotNullable().WithDefaultValue(true);
            }

            if (!Schema.Table("TorrentFiles").Column("Priority").Exists())
            {
                Alter.Table("TorrentFiles")
                    .AddColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0);
            }

            if (!Schema.Table("TorrentFiles").Column("BytesCompleted").Exists())
            {
                Alter.Table("TorrentFiles")
                    .AddColumn("BytesCompleted").AsInt64().NotNullable().WithDefaultValue(0);
            }
        }
    }

    public override void Down()
    {
    }
}
