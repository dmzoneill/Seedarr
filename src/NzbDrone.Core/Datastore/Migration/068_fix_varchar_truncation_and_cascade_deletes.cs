using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(68)]
public class FixVarcharTruncationAndCascadeDeletes : NzbDroneMigrationBase
{
    public override void Up()
    {
        // 1. Clean up any existing orphaned child rows in both SQLite and PostgreSQL
        if (Schema.Table("TorrentFiles").Exists())
        {
            Execute.Sql("DELETE FROM \"TorrentFiles\" WHERE \"TorrentId\" NOT IN (SELECT \"Id\" FROM \"Torrents\");");
        }

        if (Schema.Table("TrackerEntries").Exists())
        {
            Execute.Sql("DELETE FROM \"TrackerEntries\" WHERE \"TorrentId\" NOT IN (SELECT \"Id\" FROM \"Torrents\");");
        }

        if (Schema.Table("TorrentEventLogs").Exists())
        {
            Execute.Sql("DELETE FROM \"TorrentEventLogs\" WHERE \"TorrentId\" NOT IN (SELECT \"Id\" FROM \"Torrents\");");
        }

        if (Schema.Table("TorrentMediaMetadata").Exists())
        {
            Execute.Sql("DELETE FROM \"TorrentMediaMetadata\" WHERE \"TorrentId\" NOT IN (SELECT \"Id\" FROM \"Torrents\");");
        }

        // 2. Expand VARCHAR(255) columns to unbounded TEXT on PostgreSQL
        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("DownloadHistory").AlterColumn("MagnetUrl").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("DownloadHistory").AlterColumn("DownloadUrl").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("DownloadHistory").AlterColumn("DataJson").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("TorrentMediaMetadata").AlterColumn("Overview").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("TorrentMediaMetadata").AlterColumn("MediaInfoJson").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("TorrentMediaMetadata").AlterColumn("Cast").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("NotificationDefinitions").AlterColumn("Settings").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("TorrentEventLogs").AlterColumn("Message").AsString(int.MaxValue).NotNullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("TrackerEntries").AlterColumn("ErrorMessage").AsString(int.MaxValue).Nullable();

        IfDatabase("Postgres", "PostgreSQL")
            .Alter.Table("TrackerEntries").AlterColumn("WarningMessage").AsString(int.MaxValue).Nullable();

        // 3. Ensure Cascade Deletes on PostgreSQL foreign keys
        IfDatabase("Postgres", "PostgreSQL").Execute.Sql(@"
            DO $$
            DECLARE
                c_name text;
            BEGIN
                IF to_regclass('""TorrentFiles""') IS NOT NULL AND to_regclass('""Torrents""') IS NOT NULL THEN
                    FOR c_name IN
                        SELECT conname
                        FROM pg_constraint
                        WHERE conrelid = '""TorrentFiles""'::regclass
                          AND contype = 'f'
                    LOOP
                        EXECUTE 'ALTER TABLE ""TorrentFiles"" DROP CONSTRAINT ' || quote_ident(c_name);
                    END LOOP;

                    ALTER TABLE ""TorrentFiles""
                    ADD CONSTRAINT ""FK_TorrentFiles_TorrentId_Torrents_Id""
                    FOREIGN KEY (""TorrentId"") REFERENCES ""Torrents"" (""Id"") ON DELETE CASCADE;
                END IF;

                IF to_regclass('""TrackerEntries""') IS NOT NULL AND to_regclass('""Torrents""') IS NOT NULL THEN
                    FOR c_name IN
                        SELECT conname
                        FROM pg_constraint
                        WHERE conrelid = '""TrackerEntries""'::regclass
                          AND contype = 'f'
                    LOOP
                        EXECUTE 'ALTER TABLE ""TrackerEntries"" DROP CONSTRAINT ' || quote_ident(c_name);
                    END LOOP;

                    ALTER TABLE ""TrackerEntries""
                    ADD CONSTRAINT ""FK_TrackerEntries_TorrentId_Torrents_Id""
                    FOREIGN KEY (""TorrentId"") REFERENCES ""Torrents"" (""Id"") ON DELETE CASCADE;
                END IF;

                IF to_regclass('""TorrentEventLogs""') IS NOT NULL AND to_regclass('""Torrents""') IS NOT NULL THEN
                    FOR c_name IN
                        SELECT conname
                        FROM pg_constraint
                        WHERE conrelid = '""TorrentEventLogs""'::regclass
                          AND contype = 'f'
                    LOOP
                        EXECUTE 'ALTER TABLE ""TorrentEventLogs"" DROP CONSTRAINT ' || quote_ident(c_name);
                    END LOOP;

                    ALTER TABLE ""TorrentEventLogs""
                    ADD CONSTRAINT ""FK_TorrentEventLogs_TorrentId_Torrents_Id""
                    FOREIGN KEY (""TorrentId"") REFERENCES ""Torrents"" (""Id"") ON DELETE CASCADE;
                END IF;

                IF to_regclass('""TorrentMediaMetadata""') IS NOT NULL AND to_regclass('""Torrents""') IS NOT NULL THEN
                    FOR c_name IN
                        SELECT conname
                        FROM pg_constraint
                        WHERE conrelid = '""TorrentMediaMetadata""'::regclass
                          AND contype = 'f'
                    LOOP
                        EXECUTE 'ALTER TABLE ""TorrentMediaMetadata"" DROP CONSTRAINT ' || quote_ident(c_name);
                    END LOOP;

                    ALTER TABLE ""TorrentMediaMetadata""
                    ADD CONSTRAINT ""FK_TorrentMediaMetadata_TorrentId_Torrents_Id""
                    FOREIGN KEY (""TorrentId"") REFERENCES ""Torrents"" (""Id"") ON DELETE CASCADE;
                END IF;
            END $$;
        ");
    }

    public override void Down()
    {
    }
}
