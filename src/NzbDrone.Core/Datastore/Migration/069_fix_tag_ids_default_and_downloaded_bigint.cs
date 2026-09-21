using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(69)]
public class FixTagIdsDefaultAndDownloadedBigint : NzbDroneMigrationBase
{
    public override void Up()
    {
        // 1. Clean up any existing rows with '0', empty string, or NULL for TagIds in both SQLite and PostgreSQL
        if (Schema.Table("Torrents").Exists())
        {
            Execute.Sql("UPDATE \"Torrents\" SET \"TagIds\" = '[]' WHERE \"TagIds\" = '0' OR \"TagIds\" IS NULL OR \"TagIds\" = '';");
        }

        // 2. On PostgreSQL, safely convert Torrents.TagIds from integer to text with '[]' default if needed
        IfDatabase("Postgres", "PostgreSQL").Execute.Sql(@"
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_name = 'Torrents'
                      AND column_name = 'TagIds'
                      AND data_type IN ('integer', 'smallint', 'bigint')
                ) THEN
                    ALTER TABLE ""Torrents"" ALTER COLUMN ""TagIds"" DROP DEFAULT;
                    ALTER TABLE ""Torrents"" ALTER COLUMN ""TagIds"" TYPE text USING (
                        CASE
                            WHEN ""TagIds"" IS NULL OR ""TagIds""::text = '0' OR ""TagIds""::text = '' THEN '[]'
                            ELSE ""TagIds""::text
                        END
                    );
                END IF;
            END $$;
        ");

        // 3. Ensure Torrents.TagIds default value is '[]' and column is text (unbounded string) nullable
        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("Torrents").AlterColumn("TagIds").AsString(int.MaxValue).Nullable().WithDefaultValue("[]");

        // 4. On PostgreSQL, alter TrackerEntries.Downloaded from 32-bit integer to 64-bit bigint
        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("TrackerEntries").AlterColumn("Downloaded").AsInt64().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
