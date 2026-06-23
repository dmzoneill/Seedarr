using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(16)]
public class AddWebhookEnabled : NzbDroneMigrationBase
{
    public override void Up()
    {
        Alter.Table("ArrConnectionDefinitions")
            .AddColumn("WebhookEnabled").AsBoolean().NotNullable().WithDefaultValue(true);
    }

    public override void Down()
    {
    }
}
