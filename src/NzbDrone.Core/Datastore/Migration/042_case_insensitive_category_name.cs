using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(42)]
public class CaseInsensitiveCategoryName : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Categories").Exists())
        {
            if (Schema.Table("RssRules").Exists())
            {
                Execute.Sql(
                    """
                    UPDATE "RssRules"
                    SET "CategoryId" = (
                        SELECT MIN(c."Id")
                        FROM "Categories" c
                        WHERE LOWER(TRIM(c."Name")) = LOWER(TRIM((
                            SELECT c2."Name" FROM "Categories" c2 WHERE c2."Id" = "RssRules"."CategoryId"
                        )))
                    )
                    WHERE "CategoryId" > 0;
                    """);
            }

            if (Schema.Table("Torrents").Exists())
            {
                Execute.Sql(
                    """
                    UPDATE "Torrents"
                    SET "Category" = (
                        SELECT c."Name"
                        FROM "Categories" c
                        WHERE c."Id" = (
                            SELECT MIN(c2."Id")
                            FROM "Categories" c2
                            WHERE LOWER(TRIM(c2."Name")) = LOWER(TRIM("Torrents"."Category"))
                        )
                    )
                    WHERE "Category" IS NOT NULL AND TRIM("Category") != '';
                    """);
            }

            Execute.Sql(
                """
                DELETE FROM "Categories"
                WHERE "Id" NOT IN (
                    SELECT MIN("Id")
                    FROM "Categories"
                    GROUP BY LOWER(TRIM("Name"))
                );
                """);

            Execute.Sql("UPDATE \"Categories\" SET \"Name\" = LOWER(TRIM(\"Name\")) WHERE \"Name\" IS NOT NULL;");

            if (Schema.Table("Categories").Index("IX_Categories_Name").Exists())
            {
                Delete.Index("IX_Categories_Name").OnTable("Categories");
            }
            else
            {
                Execute.Sql("DROP INDEX IF EXISTS \"IX_Categories_Name\";");
            }

            Execute.Sql("DROP INDEX IF EXISTS \"UC_Categories_Name\";");
            Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Categories_Name\" ON \"Categories\" (LOWER(\"Name\"));");
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
