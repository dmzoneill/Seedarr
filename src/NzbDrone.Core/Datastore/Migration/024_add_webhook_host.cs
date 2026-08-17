using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(24)]
public class AddWebhookHost : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("ArrConnectionDefinitions")
            .AddColumn("WebhookHost").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
