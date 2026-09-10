using System;

namespace NzbDrone.Common.EnvironmentInfo;

public interface IRandomNumberGenerator
{
    int Next();
    int Next(int maxValue);
    int Next(int minValue, int maxValue);
    double NextDouble();
}

public class RandomNumberGenerator : IRandomNumberGenerator
{
    private readonly Random _random;

    public RandomNumberGenerator()
        : this(Random.Shared)
    {
    }

    public RandomNumberGenerator(int seed)
        : this(new Random(seed))
    {
    }

    public RandomNumberGenerator(Random random)
    {
        _random = random ?? Random.Shared;
    }

    public int Next() => _random.Next();
    public int Next(int maxValue) => _random.Next(maxValue);
    public int Next(int minValue, int maxValue) => _random.Next(minValue, maxValue);
    public double NextDouble() => _random.NextDouble();
}
