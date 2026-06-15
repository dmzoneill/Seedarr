using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(5)]
public class AddArrConnections : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("ArrConnectionDefinitions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Implementation").AsString().NotNullable()
            .WithColumn("ConfigContract").AsString().Nullable()
            .WithColumn("Settings").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("Url").AsString().NotNullable()
            .WithColumn("ApiKey").AsString().NotNullable()
            .WithColumn("ArrType").AsString().NotNullable()
            .WithColumn("SyncIntervalMinutes").AsInt32().NotNullable().WithDefaultValue(60);
    }

    public override void Down()
    {
    }
}
