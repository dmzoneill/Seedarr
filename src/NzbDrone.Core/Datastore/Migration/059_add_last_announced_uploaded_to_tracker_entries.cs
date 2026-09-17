using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(59)]
public class AddLastAnnouncedUploadedToTrackerEntries : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("TrackerEntries").Exists() && !Schema.Table("TrackerEntries").Column("LastAnnouncedUploaded").Exists())
        {
            Alter.Table("TrackerEntries")
                .AddColumn("LastAnnouncedUploaded").AsInt64().NotNullable().WithDefaultValue(0);
        }
    }

    public override void Down()
    {
    }
}
