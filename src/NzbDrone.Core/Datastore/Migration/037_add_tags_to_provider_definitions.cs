using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(37)]
public class AddTagsToProviderDefinitions : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("IndexerDefinitions").Exists() && !Schema.Table("IndexerDefinitions").Column("Tags").Exists())
        {
            Alter.Table("IndexerDefinitions")
                .AddColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");
        }

        if (Schema.Table("DownloadClientDefinitions").Exists() && !Schema.Table("DownloadClientDefinitions").Column("Tags").Exists())
        {
            Alter.Table("DownloadClientDefinitions")
                .AddColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");
        }

        if (Schema.Table("ArrConnectionDefinitions").Exists() && !Schema.Table("ArrConnectionDefinitions").Column("Tags").Exists())
        {
            Alter.Table("ArrConnectionDefinitions")
                .AddColumn("Tags").AsString().NotNullable().WithDefaultValue("[]");
        }
    }

    public override void Down()
    {
    }
}
