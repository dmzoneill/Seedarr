using System;

namespace NzbDrone.Core.Peers.SuperSeeding;

public class SuperSeedingTracker : Seeding.SuperSeedingTracker
{
    public SuperSeedingTracker()
        : base()
    {
    }

    public SuperSeedingTracker(int pieceCount, double propagationTimeoutSeconds = 90)
        : base(pieceCount, propagationTimeoutSeconds)
    {
    }

    public SuperSeedingTracker(int pieceCount, TimeSpan propagationTimeout)
        : base(pieceCount, propagationTimeout)
    {
    }
}
