using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(47)]
public class AddPriorityToRssRules : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("RssRules").Exists() && !Schema.Table("RssRules").Column("Priority").Exists())
        {
            Alter.Table("RssRules")
                .AddColumn("Priority").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }

    public override void Down()
    {
    }
}
