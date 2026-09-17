using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(58)]
public class AddAutoTaggerRules : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("AutoTaggerRules").Exists())
        {
            Create.Table("AutoTaggerRules")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("TagId").AsInt32().NotNullable()
                .WithColumn("RuleType").AsInt32().NotNullable()
                .WithColumn("Pattern").AsString().NotNullable()
                .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
                .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }

    public override void Down()
    {
    }
}
