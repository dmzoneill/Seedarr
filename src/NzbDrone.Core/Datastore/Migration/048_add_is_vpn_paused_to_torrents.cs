using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(48)]
public class AddIsVpnPausedToTorrents : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Torrents").Exists() && !Schema.Table("Torrents").Column("IsVpnPaused").Exists())
        {
            Alter.Table("Torrents")
                .AddColumn("IsVpnPaused").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }

    public override void Down()
    {
    }
}
