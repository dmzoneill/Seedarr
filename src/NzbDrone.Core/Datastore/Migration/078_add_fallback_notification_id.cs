using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(78)]
public class AddFallbackNotificationId : NzbDroneMigrationBase
{
    public override void Up()
    {
        // Nullable Int32 is valid on both SQLite and PostgreSQL. DbFactory runs this
        // migration through the matching FluentMigrator processor for each database.
        if (Schema.Table("NotificationDefinitions").Exists() &&
            !Schema.Table("NotificationDefinitions").Column("FallbackNotificationId").Exists())
        {
            Alter.Table("NotificationDefinitions")
                .AddColumn("FallbackNotificationId").AsInt32().Nullable();
        }
    }

    public override void Down()
    {
    }
}
