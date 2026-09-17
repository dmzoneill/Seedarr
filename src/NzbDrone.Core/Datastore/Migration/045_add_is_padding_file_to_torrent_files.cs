using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(45)]
public class AddIsPaddingFileToTorrentFiles : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("TorrentFiles").Exists())
        {
            if (!Schema.Table("TorrentFiles").Column("IsPaddingFile").Exists())
            {
                Alter.Table("TorrentFiles")
                    .AddColumn("IsPaddingFile").AsBoolean().NotNullable().WithDefaultValue(false);
            }
        }
    }

    public override void Down()
    {
    }
}
