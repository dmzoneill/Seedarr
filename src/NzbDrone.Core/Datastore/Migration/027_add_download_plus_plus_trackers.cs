using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(27)]
public class AddDownloadPlusPlusTrackers : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("DownloadPlusPlusTrackers")
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
            .WithColumn("Enabled").AsBoolean().NotNullable().WithDefaultValue(true);

        Create.Index("IX_DownloadPlusPlusTrackers_Url")
            .OnTable("DownloadPlusPlusTrackers")
            .OnColumn("Url");

        Create.Index("IX_DownloadPlusPlusTrackers_Status")
            .OnTable("DownloadPlusPlusTrackers")
            .OnColumn("Status");
    }

    public override void Down()
    {
    }
}
