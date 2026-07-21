using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(13)]
public class AddArrConnectionSyncFlags : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("ArrConnectionDefinitions")
            .AddColumn("SyncEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .AddColumn("EnableAutomaticAdd").AsBoolean().NotNullable().WithDefaultValue(true);
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }
}
