using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(7)]
public class AddSpeedSchedules : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("SpeedSchedules")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Days").AsInt32().NotNullable()
            .WithColumn("StartTime").AsString().NotNullable()
            .WithColumn("EndTime").AsString().NotNullable()
            .WithColumn("MaxUploadSpeed").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("MaxDownloadSpeed").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }
}
