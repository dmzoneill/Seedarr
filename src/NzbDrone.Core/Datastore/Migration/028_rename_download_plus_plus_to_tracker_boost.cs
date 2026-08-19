using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(28)]
public class RenameDownloadPlusPlusToTrackerBoost : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("DownloadPlusPlusTrackers").Exists())
        {
            Rename.Table("DownloadPlusPlusTrackers").To("TrackerBoostTrackers");
        }
        else if (!Schema.Table("TrackerBoostTrackers").Exists())
        {
            Create.Table("TrackerBoostTrackers")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Url").AsString().NotNullable()
                .WithColumn("Host").AsString().NotNullable()
                .WithColumn("Port").AsInt32().NotNullable().WithDefaultValue(80)
                .WithColumn("Protocol").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Source").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("SourceName").AsString().NotNullable().WithDefaultValue("Manual")
                .WithColumn("LatencyMs").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("LastScraped").AsDateTime().Nullable()
                .WithColumn("LastSuccess").AsDateTime().Nullable()
                .WithColumn("SuccessfulScrapes").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("FailedScrapes").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TotalSwarmsFound").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("TotalVerifiedTorrents").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Enabled").AsBoolean().NotNullable().WithDefaultValue(true);

            Create.Index("IX_TrackerBoostTrackers_Url")
                .OnTable("TrackerBoostTrackers")
                .OnColumn("Url");

            Create.Index("IX_TrackerBoostTrackers_Status")
                .OnTable("TrackerBoostTrackers")
                .OnColumn("Status");
        }
    }

    public override void Down()
    {
    }
}
