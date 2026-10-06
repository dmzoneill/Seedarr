### Summary
`GET /api/v1/torrents/{id}/logs?level=...` silently ignores unknown `level` values and defaults the filter floor to Debug instead of rejecting the query.

### Reproduction
1. `GET /api/v1/torrents/{id}/logs?level=notalevel&count=50`.

### Expected
`400 Bad Request` (consistent with strict query validation), or documented default behavior.

### Actual
`200 OK` with logs at Debug level and above because `ParseLevelRank` returns null for unknown tokens and `GetLogs` uses `?? LevelRank.Debug`.

### Code pointers
- `src/Seedarr.Api.V1/Torrents/TorrentController.cs` (`GetLogs`, ~2166-2196; `ParseLevelRank`, ~2199-2216)
- Parallel: #1055 (`LogController` invalid level) — same pattern in a different controller.

### Suggested fix
Return `400` when `level` is provided but not recognized; only apply the Debug default when the parameter is omitted.
