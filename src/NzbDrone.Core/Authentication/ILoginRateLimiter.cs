using System;

namespace NzbDrone.Core.Authentication;

public interface ILoginRateLimiter
{
    int MaxFailedAttempts { get; }

    TimeSpan LockoutDuration { get; }

    TimeSpan FailureWindow { get; }

    bool IsRateLimited(string ipAddress, out TimeSpan retryAfter);

    void RecordFailedAttempt(string ipAddress);

    void RecordSuccessfulLogin(string ipAddress);

    int GetFailedAttempts(string ipAddress);

    void Reset(string ipAddress);

    void Clear();
}
