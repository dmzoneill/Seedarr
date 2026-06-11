using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(2)]
public class AddTorrents : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("Torrents")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("InfoHash").AsString().Nullable()
            .WithColumn("TotalSize").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceLength").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Comment").AsString().Nullable()
            .WithColumn("CreatedBy").AsString().Nullable()
            .WithColumn("CreationDate").AsDateTime().Nullable()
            .WithColumn("IsPrivate").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Uploaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Downloaded").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("Ratio").AsDouble().NotNullable().WithDefaultValue(0)
            .WithColumn("Seeders").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Leechers").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("TrackerUrl").AsString().Nullable()
            .WithColumn("SourcePath").AsString().Nullable()
            .WithColumn("DateAdded").AsDateTime().NotNullable()
            .WithColumn("LastActive").AsDateTime().Nullable()
            .WithColumn("TagIds").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("TorrentFiles")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("TorrentId").AsInt32().NotNullable().ForeignKey("Torrents", "Id")
            .WithColumn("Path").AsString().NotNullable()
            .WithColumn("Size").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceOffset").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("PieceCount").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
