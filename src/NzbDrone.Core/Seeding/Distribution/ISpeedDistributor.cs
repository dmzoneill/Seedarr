namespace NzbDrone.Core.Seeding.Distribution;

public interface ISpeedDistributor
{
    string Name { get; }
    long[] Distribute(long totalBytesPerSecond, int torrentCount);
}
