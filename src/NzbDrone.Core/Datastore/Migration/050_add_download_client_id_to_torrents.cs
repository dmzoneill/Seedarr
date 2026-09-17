using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(50)]
public class AddDownloadClientIdToTorrents : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Torrents").Exists() && !Schema.Table("Torrents").Column("DownloadClientId").Exists())
        {
            Alter.Table("Torrents")
                .AddColumn("DownloadClientId").AsInt32().Nullable();
        }
    }

    public override void Down()
    {
    }
}
