using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentEventLogLevelRanksTest
{
    [Test]
    public void GetRank_should_map_known_levels_case_insensitively()
    {
        Assert.That(TorrentEventLogLevelRanks.GetRank("trace"), Is.EqualTo(TorrentEventLogLevelRanks.Trace));
        Assert.That(TorrentEventLogLevelRanks.GetRank(" TRACE "), Is.EqualTo(TorrentEventLogLevelRanks.Trace));
        Assert.That(TorrentEventLogLevelRanks.GetRank("Debug"), Is.EqualTo(TorrentEventLogLevelRanks.Debug));
        Assert.That(TorrentEventLogLevelRanks.GetRank("INFO"), Is.EqualTo(TorrentEventLogLevelRanks.Info));
        Assert.That(TorrentEventLogLevelRanks.GetRank("warn"), Is.EqualTo(TorrentEventLogLevelRanks.Warn));
        Assert.That(TorrentEventLogLevelRanks.GetRank("warning"), Is.EqualTo(TorrentEventLogLevelRanks.Warn));
        Assert.That(TorrentEventLogLevelRanks.GetRank("ERROR"), Is.EqualTo(TorrentEventLogLevelRanks.Error));
        Assert.That(TorrentEventLogLevelRanks.GetRank("fatal"), Is.EqualTo(TorrentEventLogLevelRanks.Fatal));
    }

    [Test]
    public void GetRank_should_return_null_for_blank_or_unknown_levels()
    {
        Assert.That(TorrentEventLogLevelRanks.GetRank(null), Is.Null);
        Assert.That(TorrentEventLogLevelRanks.GetRank(""), Is.Null);
        Assert.That(TorrentEventLogLevelRanks.GetRank("   "), Is.Null);
        Assert.That(TorrentEventLogLevelRanks.GetRank("verbose"), Is.Null);
    }

    [Test]
    public void SqlRankExpression_should_include_all_level_aliases()
    {
        var sql = TorrentEventLogLevelRanks.SqlRankExpression;
        Assert.That(sql, Does.Contain("'trace'"));
        Assert.That(sql, Does.Contain("'warning'"));
        Assert.That(sql, Does.Contain("ELSE -1"));
    }
}
