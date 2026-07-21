using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(6)]
public class AddNotificationDefinitions : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("NotificationDefinitions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Implementation").AsString().NotNullable()
            .WithColumn("ConfigContract").AsString().Nullable()
            .WithColumn("Settings").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("OnTorrentAdded").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("OnSeedingStarted").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("OnSeedingStopped").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("OnHealthIssue").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }
}
