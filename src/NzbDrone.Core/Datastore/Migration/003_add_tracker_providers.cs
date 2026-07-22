using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(3)]
public class AddTrackerProviders : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("TrackerProviderDefinitions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Implementation").AsString().NotNullable()
            .WithColumn("ConfigContract").AsString().Nullable()
            .WithColumn("Settings").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1);
    }

    public override void Down()
    {
    }
}
