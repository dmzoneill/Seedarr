using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(22)]
public class DropWebhookSecret : NzbDroneMigrationBase
{
    public override void Up()
    {
        Delete.Column("WebhookSecret").FromTable("ArrConnectionDefinitions");
    }

    public override void Down()
    {
    }
}
