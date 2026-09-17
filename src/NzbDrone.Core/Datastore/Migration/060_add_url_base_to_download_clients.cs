using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(60)]
public class AddUrlBaseToDownloadClients : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("DownloadClientDefinitions").Exists())
        {
            if (!Schema.Table("DownloadClientDefinitions").Column("UrlBase").Exists())
            {
                Alter.Table("DownloadClientDefinitions").AddColumn("UrlBase").AsString().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
