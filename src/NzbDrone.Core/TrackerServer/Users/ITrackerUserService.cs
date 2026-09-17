using System.Collections.Generic;

namespace NzbDrone.Core.TrackerServer.Users;

public interface ITrackerUserService
{
    TrackerUser GetByPasskey(string passkey);

    void RecordAnnounce(string passkey, long uploadedDelta, long downloadedDelta);

    TrackerUser AddUser(string username, string passkey = null);

    TrackerUser GetById(int id);

    List<TrackerUser> GetAll();

    void UpdateUser(TrackerUser user);

    bool DeleteUser(int id);
}
