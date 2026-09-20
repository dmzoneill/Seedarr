using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(62)]
public class AddQualityAndGrabHistoryToRss : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("RssRules").Exists())
        {
            if (!Schema.Table("RssRules").Column("AllowedResolutions").Exists())
            {
                Alter.Table("RssRules")
                    .AddColumn("AllowedResolutions").AsString().NotNullable().WithDefaultValue("[]");
            }

            if (!Schema.Table("RssRules").Column("AllowedSources").Exists())
            {
                Alter.Table("RssRules")
                    .AddColumn("AllowedSources").AsString().NotNullable().WithDefaultValue("[]");
            }

            if (!Schema.Table("RssRules").Column("AllowedCodecs").Exists())
            {
                Alter.Table("RssRules")
                    .AddColumn("AllowedCodecs").AsString().NotNullable().WithDefaultValue("[]");
            }

            if (!Schema.Table("RssRules").Column("SavePath").Exists())
            {
                Alter.Table("RssRules")
                    .AddColumn("SavePath").AsString().Nullable();
            }

            if (!Schema.Table("RssRules").Column("SequentialDownload").Exists())
            {
                Alter.Table("RssRules")
                    .AddColumn("SequentialDownload").AsBoolean().NotNullable().WithDefaultValue(false);
            }

            if (!Schema.Table("RssRules").Column("InitialStatus").Exists())
            {
                Alter.Table("RssRules")
                    .AddColumn("InitialStatus").AsInt32().Nullable();
            }
        }

        if (!Schema.Table("RssGrabHistory").Exists())
        {
            Create.Table("RssGrabHistory")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("ReleaseTitle").AsString().NotNullable()
                .WithColumn("IndexerName").AsString().Nullable()
                .WithColumn("RuleId").AsInt32().Nullable()
                .WithColumn("RuleName").AsString().Nullable()
                .WithColumn("InfoHash").AsString().Nullable()
                .WithColumn("Size").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("GrabTimestamp").AsDateTime().NotNullable()
                .WithColumn("Status").AsString().NotNullable().WithDefaultValue("Grabbed")
                .WithColumn("ErrorMessage").AsString().Nullable();

            Create.Index("IX_RssGrabHistory_RuleId")
                .OnTable("RssGrabHistory")
                .OnColumn("RuleId").Ascending();

            Create.Index("IX_RssGrabHistory_GrabTimestamp")
                .OnTable("RssGrabHistory")
                .OnColumn("GrabTimestamp").Ascending();

            Create.Index("IX_RssGrabHistory_InfoHash")
                .OnTable("RssGrabHistory")
                .OnColumn("InfoHash").Ascending();
        }
    }

    public override void Down()
    {
    }
}
