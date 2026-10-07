using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(76)]
public class FixScalarTagIdsJson : NzbDroneMigrationBase
{
    public override void Up()
    {
        // Repair databases that ran migration 69 before scalar TagIds were wrapped as JSON arrays.
        if (Schema.Table("Torrents").Exists())
        {
            Execute.Sql("""
                UPDATE "Torrents" SET "TagIds" = '[' || "TagIds" || ']'
                WHERE "TagIds" IS NOT NULL
                  AND "TagIds" != ''
                  AND "TagIds" != '[]'
                  AND "TagIds" NOT LIKE '[%';
                """);
        }
    }

    public override void Down()
    {
    }
}
