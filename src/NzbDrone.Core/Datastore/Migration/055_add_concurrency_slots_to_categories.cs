using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(55)]
public class AddConcurrencySlotsToCategories : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Categories").Exists())
        {
            if (!Schema.Table("Categories").Column("MaxActiveDownloads").Exists())
            {
                Alter.Table("Categories")
                    .AddColumn("MaxActiveDownloads").AsInt32().Nullable();
            }

            if (!Schema.Table("Categories").Column("MaxActiveUploads").Exists())
            {
                Alter.Table("Categories")
                    .AddColumn("MaxActiveUploads").AsInt32().Nullable();
            }

            if (!Schema.Table("Categories").Column("ReservedDownloadSlots").Exists())
            {
                Alter.Table("Categories")
                    .AddColumn("ReservedDownloadSlots").AsInt32().NotNullable().WithDefaultValue(0);
            }
        }
    }

    public override void Down()
    {
    }
}
