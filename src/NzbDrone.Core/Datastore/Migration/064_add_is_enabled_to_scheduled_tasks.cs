using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(64)]
public class AddIsEnabledToScheduledTasks : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("ScheduledTasks").Exists())
        {
            if (!Schema.Table("ScheduledTasks").Column("IsEnabled").Exists())
            {
                Alter.Table("ScheduledTasks")
                    .AddColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true);
            }
        }
    }

    public override void Down()
    {
    }
}
