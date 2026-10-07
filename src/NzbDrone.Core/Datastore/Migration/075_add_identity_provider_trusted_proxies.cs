using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(75)]
public class AddIdentityProviderTrustedProxies : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("IdentityProviders").Exists())
        {
            return;
        }

        if (!Schema.Table("IdentityProviders").Column("TrustedProxies").Exists())
        {
            Alter.Table("IdentityProviders")
                .AddColumn("TrustedProxies").AsString().Nullable();
        }
    }

    public override void Down()
    {
        if (!Schema.Table("IdentityProviders").Exists())
        {
            return;
        }

        if (Schema.Table("IdentityProviders").Column("TrustedProxies").Exists())
        {
            Delete.Column("TrustedProxies").FromTable("IdentityProviders");
        }
    }
}
