using FluentMigrator;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(56)]
public class AddFirstLastPiecePrioToTorrents : NzbDroneMigrationBase
{
    public override void Up()
    {
        if (Schema.Table("Torrents").Exists())
        {
            if (!Schema.Table("Torrents").Column("FirstLastPiecePrio").Exists())
            {
                Alter.Table("Torrents")
                    .AddColumn("FirstLastPiecePrio").AsBoolean().NotNullable().WithDefaultValue(false);
            }
        }
    }

    public override void Down()
    {
    }
}
