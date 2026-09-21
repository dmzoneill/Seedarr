namespace NzbDrone.Core.Plugins;

public enum PluginState
{
    Stopped,
    Starting,
    Running,
    Crashed,
    Errored,
    Disabled
}
