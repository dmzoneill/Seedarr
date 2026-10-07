using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(77)]
public class AddRevokedSessionsExpiresAtIndex : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("RevokedSessions").Exists() &&
            !Schema.Table("RevokedSessions").Index("IX_RevokedSessions_ExpiresAtUtc").Exists())
        {
            Create.Index("IX_RevokedSessions_ExpiresAtUtc")
                .OnTable("RevokedSessions")
                .OnColumn("ExpiresAtUtc");
        }
    }

    public override void Down()
    {
    }
}
