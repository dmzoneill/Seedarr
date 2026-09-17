using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(46)]
public class AddAllowUnknownSeedersToRssRules : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("RssRules").Exists() && !Schema.Table("RssRules").Column("AllowUnknownSeeders").Exists())
        {
            Alter.Table("RssRules")
                .AddColumn("AllowUnknownSeeders").AsBoolean().NotNullable().WithDefaultValue(true);
        }
    }

    public override void Down()
    {
    }
}
