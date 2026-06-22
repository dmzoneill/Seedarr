using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(10)]
public class AddDownloadProgress : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("Torrents")
            .AddColumn("Progress").AsDouble().NotNullable().WithDefaultValue(0.0);
    }

    public override void Down()
    {
    }
}
