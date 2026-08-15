using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(17)]
public class AddIndexerDefinitions : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("IndexerDefinitions")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Implementation").AsString().NotNullable()
            .WithColumn("ConfigContract").AsString().Nullable()
            .WithColumn("Settings").AsString().Nullable()
            .WithColumn("Enable").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Priority").AsInt32().NotNullable().WithDefaultValue(25)
            .WithColumn("IndexerType").AsString().NotNullable()
            .WithColumn("Url").AsString().NotNullable()
            .WithColumn("ApiKey").AsString().Nullable()
            .WithColumn("ApiPath").AsString().Nullable().WithDefaultValue("/api")
            .WithColumn("EnableRss").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("EnableSearch").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("Categories").AsString().Nullable()
            .WithColumn("DownloadClientId").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
