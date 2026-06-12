namespace NzbDrone.Core.Seeding.Distribution;

public class EqualDistributor : ISpeedDistributor
{
    public string Name => "Equal";

    public long[] Distribute(long totalBytesPerSecond, int torrentCount)
    {
        var speeds = new long[torrentCount];
        var perTorrent = torrentCount > 0 ? totalBytesPerSecond / torrentCount : 0L;

        for (var i = 0; i < torrentCount; i++)
        {
            speeds[i] = perTorrent;
        }

        return speeds;
    }
}
