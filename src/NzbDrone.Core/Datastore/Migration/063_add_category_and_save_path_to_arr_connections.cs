using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(63)]
public class AddCategoryAndSavePathToArrConnections : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("ArrConnectionDefinitions").Exists())
        {
            if (!Schema.Table("ArrConnectionDefinitions").Column("Category").Exists())
            {
                Alter.Table("ArrConnectionDefinitions")
                    .AddColumn("Category").AsString().Nullable();
            }

            if (!Schema.Table("ArrConnectionDefinitions").Column("SavePath").Exists())
            {
                Alter.Table("ArrConnectionDefinitions")
                    .AddColumn("SavePath").AsString().Nullable();
            }
        }
    }

    public override void Down()
    {
    }
}
