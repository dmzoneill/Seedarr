using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(25)]
public class AddTorrentSeedingTime : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("Torrents")
            .AddColumn("SeedingTime").AsInt64().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
