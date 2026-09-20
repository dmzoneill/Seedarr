using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(66)]
public class AddPerformanceAndLookupIndexes : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Torrents").Exists() &&
            !Schema.Table("Torrents").Index("IX_Torrents_Status").Exists())
        {
            Create.Index("IX_Torrents_Status")
                .OnTable("Torrents")
                .OnColumn("Status");
        }

        if (Schema.Table("Torrents").Exists() &&
            !Schema.Table("Torrents").Index("IX_Torrents_SortOrder").Exists())
        {
            Create.Index("IX_Torrents_SortOrder")
                .OnTable("Torrents")
                .OnColumn("SortOrder");
        }

        if (Schema.Table("DownloadHistory").Exists() &&
            !Schema.Table("DownloadHistory").Index("IX_DownloadHistory_InfoHash").Exists())
        {
            Create.Index("IX_DownloadHistory_InfoHash")
                .OnTable("DownloadHistory")
                .OnColumn("InfoHash");
        }

        if (Schema.Table("DownloadHistory").Exists() &&
            !Schema.Table("DownloadHistory").Index("IX_DownloadHistory_DateAdded").Exists())
        {
            Create.Index("IX_DownloadHistory_DateAdded")
                .OnTable("DownloadHistory")
                .OnColumn("DateAdded");
        }

        if (Schema.Table("PeerConnectionLogs").Exists() &&
            !Schema.Table("PeerConnectionLogs").Index("IX_PeerConnectionLogs_InfoHash_Timestamp").Exists())
        {
            Create.Index("IX_PeerConnectionLogs_InfoHash_Timestamp")
                .OnTable("PeerConnectionLogs")
                .OnColumn("InfoHash").Ascending()
                .OnColumn("Timestamp").Descending();
        }

        if (Schema.Table("TrackerMetrics").Exists() &&
            !Schema.Table("TrackerMetrics").Index("IX_TrackerMetrics_TrackerUrl").Exists())
        {
            Create.Index("IX_TrackerMetrics_TrackerUrl")
                .OnTable("TrackerMetrics")
                .OnColumn("TrackerUrl");
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
        if (Schema.Table("Torrents").Exists() &&
            Schema.Table("Torrents").Index("IX_Torrents_Status").Exists())
        {
            Delete.Index("IX_Torrents_Status").OnTable("Torrents");
        }

        if (Schema.Table("Torrents").Exists() &&
            Schema.Table("Torrents").Index("IX_Torrents_SortOrder").Exists())
        {
            Delete.Index("IX_Torrents_SortOrder").OnTable("Torrents");
        }

        if (Schema.Table("PeerConnectionLogs").Exists() &&
            Schema.Table("PeerConnectionLogs").Index("IX_PeerConnectionLogs_InfoHash_Timestamp").Exists())
        {
            Delete.Index("IX_PeerConnectionLogs_InfoHash_Timestamp").OnTable("PeerConnectionLogs");
        }
    }
}
