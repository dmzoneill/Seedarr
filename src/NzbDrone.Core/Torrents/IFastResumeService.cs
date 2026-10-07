using System.Threading.Tasks;

namespace NzbDrone.Core.Torrents;

public interface IFastResumeService
{
    void SaveAll();

    void SaveFastResume(Torrent torrent);

    void DeleteFastResume(string infoHash);

    FastResumeData LoadFastResume(string infoHash);

    FastResumeData LoadFastResume(Torrent torrent);

    void LoadAll();

    Task ScheduleBackgroundRecheck(Torrent torrent, FastResumeData data = null);
}
