using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(39)]
public class AddAcceptInvalidCertificatesToArrConnections : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("ArrConnectionDefinitions").Exists() && !Schema.Table("ArrConnectionDefinitions").Column("AcceptInvalidCertificates").Exists())
        {
            Alter.Table("ArrConnectionDefinitions")
                .AddColumn("AcceptInvalidCertificates").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }

    public override void Down()
    {
    }
}
