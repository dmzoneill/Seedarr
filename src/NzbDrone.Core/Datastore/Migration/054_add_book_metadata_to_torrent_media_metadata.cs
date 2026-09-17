using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(54)]
public class AddBookMetadataToTorrentMediaMetadata : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("TorrentMediaMetadata").Exists())
        {
            if (!Schema.Table("TorrentMediaMetadata").Column("Author").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("Author").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("BookTitle").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("BookTitle").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("Isbn").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("Isbn").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("Publisher").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("Publisher").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("PageCount").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("PageCount").AsInt32().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("PackagingFormat").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("PackagingFormat").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("SeriesName").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("SeriesName").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("SeriesPosition").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("SeriesPosition").AsString().Nullable();
            }

            if (!Schema.Table("TorrentMediaMetadata").Column("Asin").Exists())
            {
                Alter.Table("TorrentMediaMetadata").AddColumn("Asin").AsString().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
