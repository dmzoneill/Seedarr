using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(38)]
public class AddTagsToRssRules : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("RssRules").Exists() && !Schema.Table("RssRules").Column("Tags").Exists())
        {
            Alter.Table("RssRules")
                .AddColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");
        }
    }

    public override void Down()
    {
    }
}
