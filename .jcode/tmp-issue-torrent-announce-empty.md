### Summary
Manual tracker announce endpoints report `success: true` when no announce attempts were performed because `ITrackerAnnounceService` is not registered, misleading operators and automation.

### Reproduction
1. Run Seedarr in a host/DI configuration where `ITrackerAnnounceService` is null (constructor optional dependency in `TorrentController`).
2. `POST /api/v1/torrents/{id}/announce` for a valid torrent.

### Expected
`503 Service Unavailable` or `success: false` with a clear message that announce is unavailable.

### Actual
`200 OK` with `success: true` because the response treats `results.Count == 0` as success (`success = results.Count == 0 || successfulCount > 0`). `TriggerAnnounceInternal` logs a warning and returns an empty list when the service is missing.

### Code pointers
- `src/Seedarr.Api.V1/Torrents/TorrentController.cs` (`Announce`, ~1688-1730; `TriggerAnnounceInternal`, ~1631-1666)

### Suggested fix
If `_trackerAnnounceService` is null, return 503. If announce runs but returns zero tracker results for a torrent that has enabled trackers, return `success: false`.
