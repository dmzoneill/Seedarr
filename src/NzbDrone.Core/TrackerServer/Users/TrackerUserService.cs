using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NzbDrone.Core.TrackerServer.Users;

public class TrackerUserService : ITrackerUserService
{
    private readonly ConcurrentDictionary<string, TrackerUser> _usersByPasskey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, TrackerUser> _usersById = new();
    private int _nextId;

    public TrackerUser GetByPasskey(string passkey)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            return null;
        }

        return _usersByPasskey.TryGetValue(passkey.Trim(), out var user) ? user : null;
    }

    public void RecordAnnounce(string passkey, long uploadedDelta, long downloadedDelta)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            return;
        }

        var user = GetByPasskey(passkey);
        if (user == null)
        {
            return;
        }

        lock (user)
        {
            if (uploadedDelta > 0)
            {
                user.Uploaded += uploadedDelta;
            }

            if (downloadedDelta > 0)
            {
                user.Downloaded += downloadedDelta;
            }

            user.LastAnnounceAt = DateTime.UtcNow;
        }
    }

    public TrackerUser AddUser(string username, string passkey = null)
    {
        if (string.IsNullOrWhiteSpace(passkey))
        {
            passkey = Guid.NewGuid().ToString("N");
        }
        else
        {
            passkey = passkey.Trim();
        }

        var id = Interlocked.Increment(ref _nextId);
        var user = new TrackerUser
        {
            Id = id,
            Username = username,
            Passkey = passkey,
            IsEnabled = true,
            IsBanned = false,
            Uploaded = 0,
            Downloaded = 0,
            CreatedAt = DateTime.UtcNow
        };

        _usersByPasskey[passkey] = user;
        _usersById[id] = user;

        return user;
    }

    public TrackerUser GetById(int id)
    {
        return _usersById.TryGetValue(id, out var user) ? user : null;
    }

    public List<TrackerUser> GetAll()
    {
        return _usersById.Values.ToList();
    }

    public void UpdateUser(TrackerUser user)
    {
        if (user == null)
        {
            return;
        }

        if (_usersById.TryGetValue(user.Id, out var existing))
        {
            if (!string.Equals(existing.Passkey, user.Passkey, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(existing.Passkey))
            {
                _usersByPasskey.TryRemove(existing.Passkey.Trim(), out _);
            }
        }

        _usersById[user.Id] = user;
        if (!string.IsNullOrWhiteSpace(user.Passkey))
        {
            _usersByPasskey[user.Passkey.Trim()] = user;
        }
    }

    public bool DeleteUser(int id)
    {
        if (_usersById.TryRemove(id, out var user))
        {
            if (!string.IsNullOrWhiteSpace(user.Passkey))
            {
                _usersByPasskey.TryRemove(user.Passkey.Trim(), out _);
            }

            return true;
        }

        return false;
    }
}
