using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(67)]
public class AddRemotePathMappings : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (!Schema.Table("RemotePathMappings").Exists())
        {
            Create.Table("RemotePathMappings")
                .WithColumn("Id").AsInt32().PrimaryKey().Identity()
                .WithColumn("Host").AsString().NotNullable()
                .WithColumn("RemotePath").AsString().NotNullable()
                .WithColumn("LocalPath").AsString().NotNullable();
        }
    }

    public override void Down()
    {
    }
}
