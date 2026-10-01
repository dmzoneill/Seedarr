using System;

namespace NzbDrone.Common.EnvironmentInfo;

public interface IRandomNumberGenerator
{
    int Next();
    int Next(int maxValue);
    int Next(int minValue, int maxValue);
    double NextDouble();
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "RandomNumberGenerator is a mockable wrapper for non-cryptographic seeding simulation")]
public class RandomNumberGenerator : IRandomNumberGenerator
{
    private readonly Random _random;

    public RandomNumberGenerator()
        : this(Random.Shared)
    {
    }

    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Seeded pseudo-random generator is required for deterministic simulation and tests")]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("csharpsquid", "S2245", Justification = "Seeded pseudo-random generator is required for deterministic simulation and tests")]
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
