using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(70)]
public class AddRevokedSessions : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("RevokedSessions").Exists())
        {
            Create.Table("RevokedSessions")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("SessionKey").AsString().NotNullable()
                .WithColumn("RevokedAtUtc").AsDateTime().NotNullable()
                .WithColumn("ExpiresAtUtc").AsDateTime().NotNullable();

            Create.Index("IX_RevokedSessions_SessionKey")
                .OnTable("RevokedSessions")
                .OnColumn("SessionKey").Ascending()
                .WithOptions().Unique();
        }
    }

    public override void Down()
    {
    }
}
