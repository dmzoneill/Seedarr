using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(19)]
public class AddWebhookSecret : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("ArrConnectionDefinitions")
            .AddColumn("WebhookSecret").AsString().Nullable();
    }

    public override void Down()
    {
    }
}
