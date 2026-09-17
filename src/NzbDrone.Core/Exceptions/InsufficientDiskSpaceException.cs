using System;

namespace NzbDrone.Core.Exceptions;

/// <summary>
/// Exception thrown when a torrent cannot be started or added due to insufficient disk space on the destination volume.
/// </summary>
public class InsufficientDiskSpaceException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InsufficientDiskSpaceException"/> class.
    /// </summary>
    public InsufficientDiskSpaceException()
        : base("Insufficient disk space on destination volume to complete torrent download")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InsufficientDiskSpaceException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InsufficientDiskSpaceException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InsufficientDiskSpaceException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public InsufficientDiskSpaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
