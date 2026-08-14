using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(14)]
public class AddTorrentSortOrder : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("Torrents")
            .AddColumn("SortOrder").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
