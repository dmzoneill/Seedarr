using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(43)]
public class AddTorrentShareLimits : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Torrents").Exists())
        {
            if (!Schema.Table("Torrents").Column("RatioLimit").Exists())
            {
                Alter.Table("Torrents")
                    .AddColumn("RatioLimit").AsDouble().Nullable();
            }

            if (!Schema.Table("Torrents").Column("SeedingTimeLimit").Exists())
            {
                Alter.Table("Torrents")
                    .AddColumn("SeedingTimeLimit").AsInt32().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
