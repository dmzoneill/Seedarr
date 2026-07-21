using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(9)]
public class AddTorrentOptions : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("Torrents")
            .AddColumn("Priority").AsInt32().NotNullable().WithDefaultValue(1)
            .AddColumn("UploadLimit").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("DownloadLimit").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("SuperSeeding").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("ForceStart").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("Label").AsString().Nullable();
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }
}
