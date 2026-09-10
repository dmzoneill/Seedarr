using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(34)]
public class UpdateNotificationsAndRssRules : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("NotificationDefinitions").Exists())
        {
            if (!Schema.Table("NotificationDefinitions").Column("OnGrab").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnGrab").AsBoolean().NotNullable().WithDefaultValue(true);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnDownloadComplete").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnDownloadComplete").AsBoolean().NotNullable().WithDefaultValue(true);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnMediaInspected").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnMediaInspected").AsBoolean().NotNullable().WithDefaultValue(false);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnExtractComplete").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnExtractComplete").AsBoolean().NotNullable().WithDefaultValue(false);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnSeedGoalReached").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnSeedGoalReached").AsBoolean().NotNullable().WithDefaultValue(true);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnTorrentDeleted").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnTorrentDeleted").AsBoolean().NotNullable().WithDefaultValue(false);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnHealthRestored").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnHealthRestored").AsBoolean().NotNullable().WithDefaultValue(true);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnManualInteractionRequired").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnManualInteractionRequired").AsBoolean().NotNullable().WithDefaultValue(true);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnApplicationUpdate").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnApplicationUpdate").AsBoolean().NotNullable().WithDefaultValue(false);
            }

            if (!Schema.Table("NotificationDefinitions").Column("Tags").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");
            }
        }
        else
        {
            Create.Table("NotificationDefinitions")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("Implementation").AsString().NotNullable()
                .WithColumn("ConfigContract").AsString().Nullable()
                .WithColumn("Settings").AsString().Nullable()
                .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnGrab").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnDownloadComplete").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnMediaInspected").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnExtractComplete").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnSeedGoalReached").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnTorrentDeleted").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("OnHealthIssue").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnHealthRestored").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnManualInteractionRequired").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("OnApplicationUpdate").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");
        }

        if (!Schema.Table("RssRules").Exists())
        {
            Create.Table("RssRules")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("MustContain").AsString().Nullable()
                .WithColumn("MustNotContain").AsString().Nullable()
                .WithColumn("MinSeeders").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("MinSizeBytes").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("MaxSizeBytes").AsInt64().NotNullable().WithDefaultValue(0)
                .WithColumn("MaxAgeDays").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("FreeleechOnly").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("CategoryId").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("IndexerIds").AsString().NotNullable().WithDefaultValue("[]");
        }
    }

    public override void Down()
    {
    }
}
