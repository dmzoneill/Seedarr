using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(57)]
public class AddPolicyAndColorToTags : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Tags").Exists())
        {
            if (!Schema.Table("Tags").Column("Color").Exists())
            {
                Alter.Table("Tags").AddColumn("Color").AsString().Nullable();
            }

            if (!Schema.Table("Tags").Column("UploadLimitKbps").Exists())
            {
                Alter.Table("Tags").AddColumn("UploadLimitKbps").AsInt32().Nullable();
            }

            if (!Schema.Table("Tags").Column("DownloadLimitKbps").Exists())
            {
                Alter.Table("Tags").AddColumn("DownloadLimitKbps").AsInt32().Nullable();
            }

            if (!Schema.Table("Tags").Column("MinSeedRatio").Exists())
            {
                Alter.Table("Tags").AddColumn("MinSeedRatio").AsDouble().Nullable();
            }

            if (!Schema.Table("Tags").Column("MinSeedTimeSeconds").Exists())
            {
                Alter.Table("Tags").AddColumn("MinSeedTimeSeconds").AsInt32().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
