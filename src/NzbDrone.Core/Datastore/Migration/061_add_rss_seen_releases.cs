using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(61)]
public class AddRssSeenReleases : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("RssSeenReleases").Exists())
        {
            Create.Table("RssSeenReleases")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("IndexerId").AsInt32().NotNullable()
                .WithColumn("Guid").AsString().NotNullable().WithDefaultValue("")
                .WithColumn("InfoHash").AsString().Nullable()
                .WithColumn("PublishDate").AsDateTime().Nullable()
                .WithColumn("FirstSeenUtc").AsDateTime().NotNullable()
                .WithColumn("Status").AsInt32().NotNullable().WithDefaultValue(1)
                .WithColumn("MatchedRuleId").AsInt32().Nullable();

            Create.Index("IX_RssSeenReleases_IndexerId_Guid")
                .OnTable("RssSeenReleases")
                .OnColumn("IndexerId").Ascending()
                .OnColumn("Guid").Ascending();

            Create.Index("IX_RssSeenReleases_IndexerId_InfoHash")
                .OnTable("RssSeenReleases")
                .OnColumn("IndexerId").Ascending()
                .OnColumn("InfoHash").Ascending();

            Create.Index("IX_RssSeenReleases_FirstSeenUtc")
                .OnTable("RssSeenReleases")
                .OnColumn("FirstSeenUtc").Ascending();
        }
    }

    public override void Down()
    {
    }
}
