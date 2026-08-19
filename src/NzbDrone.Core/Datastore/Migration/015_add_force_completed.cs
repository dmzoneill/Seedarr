using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(15)]
public class AddForceCompleted : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("Torrents")
            .AddColumn("ForceCompleted").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }
}
