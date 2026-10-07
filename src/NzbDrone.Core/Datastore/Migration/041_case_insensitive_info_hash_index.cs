using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(41)]
public class CaseInsensitiveInfoHashIndex : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Torrents").Exists())
        {
            if (Schema.Table("DownloadHistory").Exists())
            {
                Execute.Sql(
                    """
                    UPDATE "DownloadHistory"
                    SET "TorrentId" = (
                        SELECT MIN(t."Id")
                        FROM "Torrents" t
                        WHERE t."InfoHash" IS NOT NULL
                            AND LOWER(TRIM(t."InfoHash")) = LOWER(TRIM((
                                SELECT t2."InfoHash" FROM "Torrents" t2 WHERE t2."Id" = "DownloadHistory"."TorrentId"
                            )))
                    )
                    WHERE "TorrentId" IS NOT NULL;
                    """);
            }

            if (Schema.Table("TorrentFiles").Exists())
            {
                Execute.Sql(
                    """
                    DELETE FROM "TorrentFiles"
                    WHERE "TorrentId" IN (
                        SELECT t."Id"
                        FROM "Torrents" t
                        WHERE t."InfoHash" IS NOT NULL
                            AND t."Id" NOT IN (
                                SELECT MIN(t2."Id")
                                FROM "Torrents" t2
                                WHERE t2."InfoHash" IS NOT NULL
                                GROUP BY LOWER(TRIM(t2."InfoHash"))
                            )
                    );
                    """);
            }

            if (Schema.Table("TrackerEntries").Exists())
            {
                Execute.Sql(
                    """
                    DELETE FROM "TrackerEntries"
                    WHERE "TorrentId" IN (
                        SELECT t."Id"
                        FROM "Torrents" t
                        WHERE t."InfoHash" IS NOT NULL
                            AND t."Id" NOT IN (
                                SELECT MIN(t2."Id")
                                FROM "Torrents" t2
                                WHERE t2."InfoHash" IS NOT NULL
                                GROUP BY LOWER(TRIM(t2."InfoHash"))
                            )
                    );
                    """);
            }

            if (Schema.Table("TorrentEventLogs").Exists())
            {
                Execute.Sql(
                    """
                    DELETE FROM "TorrentEventLogs"
                    WHERE "TorrentId" IN (
                        SELECT t."Id"
                        FROM "Torrents" t
                        WHERE t."InfoHash" IS NOT NULL
                            AND t."Id" NOT IN (
                                SELECT MIN(t2."Id")
                                FROM "Torrents" t2
                                WHERE t2."InfoHash" IS NOT NULL
                                GROUP BY LOWER(TRIM(t2."InfoHash"))
                            )
                    );
                    """);
            }

            if (Schema.Table("TorrentMediaMetadata").Exists())
            {
                Execute.Sql(
                    """
                    DELETE FROM "TorrentMediaMetadata"
                    WHERE "TorrentId" IN (
                        SELECT t."Id"
                        FROM "Torrents" t
                        WHERE t."InfoHash" IS NOT NULL
                            AND t."Id" NOT IN (
                                SELECT MIN(t2."Id")
                                FROM "Torrents" t2
                                WHERE t2."InfoHash" IS NOT NULL
                                GROUP BY LOWER(TRIM(t2."InfoHash"))
                            )
                    );
                    """);
            }

            Execute.Sql(
                """
                DELETE FROM "Torrents"
                WHERE "InfoHash" IS NOT NULL
                    AND "Id" NOT IN (
                        SELECT MIN("Id")
                        FROM "Torrents"
                        WHERE "InfoHash" IS NOT NULL
                        GROUP BY LOWER(TRIM("InfoHash"))
                    );
                """);

            Execute.Sql("UPDATE \"Torrents\" SET \"InfoHash\" = LOWER(TRIM(\"InfoHash\")) WHERE \"InfoHash\" IS NOT NULL;");

            if (Schema.Table("Torrents").Index("IX_Torrents_InfoHash").Exists())
            {
                Delete.Index("IX_Torrents_InfoHash").OnTable("Torrents");
            }
            else
            {
                Execute.Sql("DROP INDEX IF EXISTS \"IX_Torrents_InfoHash\";");
            }

            Execute.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Torrents_InfoHash\" ON \"Torrents\" (LOWER(\"InfoHash\"));");
        }
    }

    public override void Down()
    {
        if (Schema.Table("Torrents").Exists())
        {
            Execute.Sql("DROP INDEX IF EXISTS \"IX_Torrents_InfoHash\";");
            Create.Index("IX_Torrents_InfoHash")
                .OnTable("Torrents")
                .OnColumn("InfoHash");
        }
    }
}
