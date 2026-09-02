using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(31)]
public class AddMissingForeignKeyAndLookupIndexes : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Index("IX_Torrents_InfoHash")
            .OnTable("Torrents")
            .OnColumn("InfoHash");

        Create.Index("IX_TorrentFiles_TorrentId")
            .OnTable("TorrentFiles")
            .OnColumn("TorrentId");

        Create.Index("IX_TrackerEntries_TorrentId")
            .OnTable("TrackerEntries")
            .OnColumn("TorrentId");

        Create.Index("IX_DownloadHistory_TorrentId")
            .OnTable("DownloadHistory")
            .OnColumn("TorrentId");
    }

    public override void Down()
    {
        Delete.Index("IX_Torrents_InfoHash").OnTable("Torrents");
        Delete.Index("IX_TorrentFiles_TorrentId").OnTable("TorrentFiles");
        Delete.Index("IX_TrackerEntries_TorrentId").OnTable("TrackerEntries");
        Delete.Index("IX_DownloadHistory_TorrentId").OnTable("DownloadHistory");
    }
}
