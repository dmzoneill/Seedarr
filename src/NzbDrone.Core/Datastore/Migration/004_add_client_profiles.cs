using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(4)]
public class AddClientProfiles : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("ClientProfileDefinitions")
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
