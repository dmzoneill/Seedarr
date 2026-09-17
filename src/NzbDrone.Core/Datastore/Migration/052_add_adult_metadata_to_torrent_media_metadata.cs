using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(52)]
public class AddAdultMetadataToTorrentMediaMetadata : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("TorrentMediaMetadata").Exists())
        {
            if (!Schema.Table("TorrentMediaMetadata").Column("Studio").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("Studio").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("SiteName").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("SiteName").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("Performers").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("Performers").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("SceneCode").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("SceneCode").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("ReleaseDate").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("ReleaseDate").AsDateTime().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
