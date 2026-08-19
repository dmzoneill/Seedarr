using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(29)]
public class AddTotalVerifiedTorrentsToTrackerBoostTrackers : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("TrackerBoostTrackers").Exists() && !Schema.Table("TrackerBoostTrackers").Column("TotalVerifiedTorrents").Exists())
        {
            Alter.Table("TrackerBoostTrackers")
                .AddColumn("TotalVerifiedTorrents").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }

    public override void Down()
    {
    }
}
