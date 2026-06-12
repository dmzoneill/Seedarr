using System;

namespace NzbDrone.Core.Seeding.Distribution;

public class ParetoDistributor : ISpeedDistributor
{
    public string Name => "Pareto";

    public long[] Distribute(long totalBytesPerSecond, int torrentCount)
    {
        var speeds = new long[torrentCount];
        if (torrentCount == 0)
        {
            return speeds;
        }

        var alpha = 1.16;
        var totalWeight = 0.0;
        var weights = new double[torrentCount];

        for (var i = 0; i < torrentCount; i++)
        {
            weights[i] = Math.Pow(1.0 / (i + 1), alpha);
            totalWeight += weights[i];
        }

        for (var i = 0; i < torrentCount; i++)
        {
            speeds[i] = (long)(totalBytesPerSecond * (weights[i] / totalWeight));
        }

        return speeds;
    }
}
