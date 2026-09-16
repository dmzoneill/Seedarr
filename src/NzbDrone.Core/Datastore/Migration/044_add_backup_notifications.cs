using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(44)]
public class AddBackupNotifications : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("NotificationDefinitions").Exists())
        {
            if (!Schema.Table("NotificationDefinitions").Column("OnBackupComplete").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnBackupComplete").AsBoolean().NotNullable().WithDefaultValue(false);
            }

            if (!Schema.Table("NotificationDefinitions").Column("OnBackupFailed").Exists())
            {
                Alter.Table("NotificationDefinitions")
                    .AddColumn("OnBackupFailed").AsBoolean().NotNullable().WithDefaultValue(true);
            }
        }
    }

    public override void Down()
    {
    }
}
