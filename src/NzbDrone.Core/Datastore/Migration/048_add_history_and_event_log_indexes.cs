using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(48)]
public class AddHistoryAndEventLogIndexes : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("DownloadHistory").Exists() &&
            !Schema.Table("DownloadHistory").Index("IX_DownloadHistory_TorrentId").Exists())
        {
            Create.Index("IX_DownloadHistory_TorrentId")
                .OnTable("DownloadHistory")
                .OnColumn("TorrentId");
        }

        if (Schema.Table("TorrentEventLogs").Exists() &&
            !Schema.Table("TorrentEventLogs").Index("IX_TorrentEventLogs_TorrentId_TimeStamp").Exists())
        {
            Create.Index("IX_TorrentEventLogs_TorrentId_TimeStamp")
                .OnTable("TorrentEventLogs")
                .OnColumn("TorrentId").Ascending()
                .OnColumn("TimeStamp").Descending();
        }
    }

    public override void Down()
    {
        if (Schema.Table("TorrentEventLogs").Exists() &&
            Schema.Table("TorrentEventLogs").Index("IX_TorrentEventLogs_TorrentId_TimeStamp").Exists())
        {
            Delete.Index("IX_TorrentEventLogs_TorrentId_TimeStamp").OnTable("TorrentEventLogs");
        }
    }
}
