using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(79)]
public class SeedClientProfileDefinitions : NzbDroneMigrationBase
{
    public override void Up()
    {
        IfDatabase("sqlite", "SQLite").Execute.Sql(InsertSql("1"));
        IfDatabase("postgres", "Postgres", "PostgreSQL").Execute.Sql(InsertSql("TRUE"));
    }

    public override void Down()
    {
        // Downgrades are not supported; this migration is intentionally irreversible.
    }

    private static string InsertSql(string enabledLiteral)
    {
        return $@"
INSERT INTO ""ClientProfileDefinitions"" (""Name"", ""Implementation"", ""ConfigContract"", ""Settings"", ""Enable"", ""Priority"")
SELECT 'qBittorrent 4.4.2', 'QBittorrentProfile', 'QBittorrentProfileSettings', NULL, {enabledLiteral}, 1
WHERE NOT EXISTS (SELECT 1 FROM ""ClientProfileDefinitions"" WHERE ""Implementation"" = 'QBittorrentProfile' OR ""Name"" = 'qBittorrent 4.4.2');

INSERT INTO ""ClientProfileDefinitions"" (""Name"", ""Implementation"", ""ConfigContract"", ""Settings"", ""Enable"", ""Priority"")
SELECT 'Deluge 2.0.3', 'DelugeProfile', 'DelugeProfileSettings', NULL, {enabledLiteral}, 1
WHERE NOT EXISTS (SELECT 1 FROM ""ClientProfileDefinitions"" WHERE ""Implementation"" = 'DelugeProfile' OR ""Name"" = 'Deluge 2.0.3');

INSERT INTO ""ClientProfileDefinitions"" (""Name"", ""Implementation"", ""ConfigContract"", ""Settings"", ""Enable"", ""Priority"")
SELECT 'Transmission 3.00', 'TransmissionProfile', 'TransmissionProfileSettings', NULL, {enabledLiteral}, 1
WHERE NOT EXISTS (SELECT 1 FROM ""ClientProfileDefinitions"" WHERE ""Implementation"" = 'TransmissionProfile' OR ""Name"" = 'Transmission 3.00');

INSERT INTO ""ClientProfileDefinitions"" (""Name"", ""Implementation"", ""ConfigContract"", ""Settings"", ""Enable"", ""Priority"")
SELECT 'uTorrent 3.5.5', 'UTorrentProfile', 'UTorrentProfileSettings', NULL, {enabledLiteral}, 1
WHERE NOT EXISTS (SELECT 1 FROM ""ClientProfileDefinitions"" WHERE ""Implementation"" = 'UTorrentProfile' OR ""Name"" = 'uTorrent 3.5.5');

INSERT INTO ""ClientProfileDefinitions"" (""Name"", ""Implementation"", ""ConfigContract"", ""Settings"", ""Enable"", ""Priority"")
SELECT 'BiglyBT 2.7.0.0', 'BiglyBTProfile', 'BiglyBTProfileSettings', NULL, {enabledLiteral}, 1
WHERE NOT EXISTS (SELECT 1 FROM ""ClientProfileDefinitions"" WHERE ""Implementation"" = 'BiglyBTProfile' OR ""Name"" = 'BiglyBT 2.7.0.0');
";
    }
}
