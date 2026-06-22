using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(12)]
public class AddTorrentRuntimeFields : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("Torrents")
            .AddColumn("SequentialDownload").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("AnnounceInterval").AsInt32().NotNullable().WithDefaultValue(1800)
            .AddColumn("NextUpdate").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("SessionUploaded").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("SessionDownloaded").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("SmallTorrentLimit").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("Threshold").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("UploadSpeed").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("DownloadSpeed").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("Active").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("Availability").AsDouble().NotNullable().WithDefaultValue(0.0)
            .AddColumn("Eta").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
