using System;

namespace NzbDrone.Core.Seeding.Distribution;

public class PowerLawDistributor : ISpeedDistributor
{
    private readonly double _alpha;

    public string Name => "PowerLaw";

    public PowerLawDistributor()
        : this(1.5)
    {
    }

    public PowerLawDistributor(double alpha)
    {
        _alpha = Math.Clamp(alpha, 0.5, 3.0);
    }

    public long[] Distribute(long totalBytesPerSecond, int torrentCount)
    {
        var speeds = new long[torrentCount];
        if (torrentCount == 0)
        {
            return speeds;
        }

        var totalWeight = 0.0;
        var weights = new double[torrentCount];

        for (var i = 0; i < torrentCount; i++)
        {
            weights[i] = 1.0 / Math.Pow(i + 1, _alpha);
            totalWeight += weights[i];
        }

        for (var i = 0; i < torrentCount; i++)
        {
            speeds[i] = (long)(totalBytesPerSecond * (weights[i] / totalWeight));
        }

        return speeds;
    }
}
