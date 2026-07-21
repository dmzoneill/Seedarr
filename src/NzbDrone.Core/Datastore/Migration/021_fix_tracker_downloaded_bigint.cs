using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(21)]
public class FixTrackerDownloadedBigint : NzbDroneMigrationBase
{
    public override void Up()
    {
        // SQLite natively stores all integer values with sufficient width;
        // no schema change needed. Non-SQLite databases may require AlterColumn.
    }

    public override void Down()
    {
    }
}
