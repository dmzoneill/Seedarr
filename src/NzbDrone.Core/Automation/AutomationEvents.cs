using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Automation;

public class AutomationScriptExecutedEvent : IEvent
{
    public int ScriptId { get; set; }
    public string ScriptName { get; set; }
    public AutomationTrigger Trigger { get; set; }
    public int? TorrentId { get; set; }
    public bool Success { get; set; }
    public long DurationMs { get; set; }
    public string Error { get; set; }

    public AutomationScriptExecutedEvent()
    {
    }

    public AutomationScriptExecutedEvent(int scriptId, string scriptName, AutomationTrigger trigger, int? torrentId, bool success, long durationMs, string error = null)
    {
        ScriptId = scriptId;
        ScriptName = scriptName;
        Trigger = trigger;
        TorrentId = torrentId;
        Success = success;
        DurationMs = durationMs;
        Error = error;
    }
}

public class AutomationScriptTriggeredEvent : IEvent
{
    public int ScriptId { get; set; }
    public string ScriptName { get; set; }
    public AutomationTrigger Trigger { get; set; }
    public int? TorrentId { get; set; }

    public AutomationScriptTriggeredEvent()
    {
    }

    public AutomationScriptTriggeredEvent(int scriptId, string scriptName, AutomationTrigger trigger, int? torrentId = null)
    {
        ScriptId = scriptId;
        ScriptName = scriptName;
        Trigger = trigger;
        TorrentId = torrentId;
    }
}

public class AutomationActionDispatchedEvent : IEvent
{
    public string ActionName { get; set; }
    public string Target { get; set; }
    public string Details { get; set; }

    public AutomationActionDispatchedEvent()
    {
    }

    public AutomationActionDispatchedEvent(string actionName, string target, string details = null)
    {
        ActionName = actionName;
        Target = target;
        Details = details;
    }
}
