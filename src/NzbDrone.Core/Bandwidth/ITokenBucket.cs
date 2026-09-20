using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Bandwidth;

public interface ITokenBucket
{
    long Rate { get; set; }
    long MaxBurstBytes { get; }
    double AvailableTokens { get; }
    bool IsUnlimited { get; }

    void SetRate(long bytesPerSecond, long? maxBurstBytes = null);
    bool TryConsume(long bytes);
    void ConsumeOrWait(long bytes);
    Task ConsumeOrWaitAsync(long bytes, CancellationToken cancellationToken = default);
    void Refund(long bytes);
    void Reset();
}
