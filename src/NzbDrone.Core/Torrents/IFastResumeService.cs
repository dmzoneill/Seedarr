namespace NzbDrone.Core.Torrents;

public interface IFastResumeService
{
    void SaveAll();

    void SaveFastResume(Torrent torrent);

    FastResumeData LoadFastResume(string infoHash);
}
