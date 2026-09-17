namespace NzbDrone.Core.Peers;

public interface IPeerServer
{
    bool IsListening { get; }

    bool BindFailed { get; }

    int ListeningPort { get; }

    string BindErrorMessage { get; }

    void StopListening();
}
