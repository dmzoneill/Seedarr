using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(65)]
public class AddLastStatusAndLastErrorToScheduledTasks : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("ScheduledTasks").Exists())
        {
            if (!Schema.Table("ScheduledTasks").Column("LastStatus").Exists())
            {
                Alter.Table("ScheduledTasks")
                    .AddColumn("LastStatus").AsInt32().NotNullable().WithDefaultValue(0);
            }

            if (!Schema.Table("ScheduledTasks").Column("LastErrorMessage").Exists())
            {
                Alter.Table("ScheduledTasks")
                    .AddColumn("LastErrorMessage").AsString().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
