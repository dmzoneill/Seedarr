using System;

namespace NzbDrone.Core.Exceptions;

public class DuplicateTagException : InvalidOperationException
{
    public string Label { get; }

    public DuplicateTagException(string label)
        : base($"Tag with label '{label}' already exists.")
    {
        Label = label;
    }

    public DuplicateTagException(string label, string message)
        : base(message)
    {
        Label = label;
    }

    public DuplicateTagException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
