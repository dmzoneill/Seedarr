using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(20)]
public class ConvertTagIdsToJson : NzbDroneMigrationBase
{
    public override void Up()
    {
        Execute.Sql("UPDATE \"Torrents\" SET \"TagIds\" = '[]' WHERE \"TagIds\" = '0' OR \"TagIds\" IS NULL OR \"TagIds\" = ''");
    }

    public override void Down()
    {
    }
}
