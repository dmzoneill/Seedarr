using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(42)]
public class CaseInsensitiveCategoryName : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Categories").Exists())
        {
            if (Schema.Table("Categories").Index("IX_Categories_Name").Exists())
            {
                Delete.Index("IX_Categories_Name").OnTable("Categories");
            }
            else
            {
                Execute.Sql("DROP INDEX IF EXISTS \"IX_Categories_Name\";");
            }

            Execute.Sql("DROP INDEX IF EXISTS \"UC_Categories_Name\";");
            Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Categories_Name\" ON \"Categories\" (\"Name\" COLLATE NOCASE);");
        }
    }

    public override void Down()
    {
        if (Schema.Table("Categories").Exists())
        {
            Execute.Sql("DROP INDEX IF EXISTS \"IX_Categories_Name\";");
            Create.Index("IX_Categories_Name")
                .OnTable("Categories")
                .OnColumn("Name")
                .Unique();
        }
    }
}
