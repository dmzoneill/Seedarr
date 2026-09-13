using System;

namespace NzbDrone.Common.Instrumentation;

public class LogEntryRecord
{
    public int Id { get; set; }
    public DateTime Time { get; set; }
    public string Level { get; set; }
    public string Logger { get; set; }
    public string Message { get; set; }
    public string Exception { get; set; }
}
