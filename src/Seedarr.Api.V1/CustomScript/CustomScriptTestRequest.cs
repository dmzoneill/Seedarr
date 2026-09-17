namespace Seedarr.Api.V1.CustomScript;

public class CustomScriptTestRequest
{
    public string ScriptPath { get; set; }

    public string Arguments { get; set; }

    public string EventType { get; set; } = "Test";
}
