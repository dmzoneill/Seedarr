using System;

namespace NzbDrone.Core.Indexers;

public class IndexerException : Exception
{
    public string ErrorCode { get; }
    public int? StatusCode { get; }
    public bool Recorded { get; set; }
    public TimeSpan? RetryAfter { get; }

    public IndexerException(string message)
        : base(message)
    {
    }

    public IndexerException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
        if (int.TryParse(errorCode, out var code))
        {
            StatusCode = code;
        }
    }

    public IndexerException(string message, string errorCode, int? statusCode, TimeSpan? retryAfter = null)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public IndexerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
