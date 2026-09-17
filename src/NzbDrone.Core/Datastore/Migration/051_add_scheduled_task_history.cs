using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(51)]
public class AddScheduledTaskHistory : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("ScheduledTaskHistory")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TaskId").AsInt32().NotNullable()
            .WithColumn("TypeName").AsString().NotNullable()
            .WithColumn("StartedAt").AsDateTime().NotNullable()
            .WithColumn("FinishedAt").AsDateTime().NotNullable()
            .WithColumn("DurationMs").AsInt64().NotNullable()
            .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("TriggerSource").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ErrorMessage").AsString().Nullable()
            .WithColumn("ExceptionDetails").AsString().Nullable();

        Create.Index("IX_ScheduledTaskHistory_TaskId")
            .OnTable("ScheduledTaskHistory")
            .OnColumn("TaskId");

        Create.Index("IX_ScheduledTaskHistory_TypeName")
            .OnTable("ScheduledTaskHistory")
            .OnColumn("TypeName");

        Create.Index("IX_ScheduledTaskHistory_StartedAt")
            .OnTable("ScheduledTaskHistory")
            .OnColumn("StartedAt");
    }

    public override void Down()
    {
    }
}
