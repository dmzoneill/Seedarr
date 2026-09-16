using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(40)]
public class AddCategoriesToNotificationDefinitions : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("NotificationDefinitions").Exists() && !Schema.Table("NotificationDefinitions").Column("Categories").Exists())
        {
            Alter.Table("NotificationDefinitions")
                .AddColumn("Categories").AsString().NotNullable().WithDefaultValue("[]");
        }
    }

    public override void Down()
    {
    }
}
