using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(33)]
public class AddIdentityProviders : NzbDroneMigrationBase
{
    public override void Up()
    {
        Create.Table("IdentityProviders")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("ProviderId").AsString(100).NotNullable().Unique()
            .WithColumn("Name").AsString(255).NotNullable()
            .WithColumn("ProviderType").AsInt32().NotNullable()
            .WithColumn("IsEnabled").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("ClientId").AsString(500).Nullable()
            .WithColumn("ClientSecretEncrypted").AsString(1000).Nullable()
            .WithColumn("IssuerUrl").AsString(1000).Nullable()
            .WithColumn("MetadataUrl").AsString(1000).Nullable()
            .WithColumn("Scopes").AsString(500).Nullable()
            .WithColumn("Certificate").AsString().Nullable()
            .WithColumn("RoleMappingRules").AsString().Nullable()
            .WithColumn("IconUrl").AsString(1000).Nullable()
            .WithColumn("ButtonText").AsString(255).Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("UpdatedAt").AsDateTime().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("IdentityProviders");
    }
}
