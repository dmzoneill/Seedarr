### Summary
`PUT /api/v1/torrents/{id}` maps the body with `TorrentResourceMapper.ToModel` and applies it via `Torrent.ApplyUserFields`. Several user-editable fields are assigned unconditionally from the deserialized resource, so a minimal JSON body that only changes one field (but includes `name`) resets other settings to CLR defaults.

### Reproduction
1. Create or pick a torrent with non-default `priority`, `uploadLimit`, or `downloadLimit` (e.g. priority 5, uploadLimit 100).
2. `PUT /api/v1/torrents/{id}` with body `{ "name": "<same name>", "label": "test" }` (omit limits/priority).
3. GET the torrent.

### Expected
Unspecified fields keep their previous values (partial update semantics).

### Actual
`priority` becomes `0`, `uploadLimit`/`downloadLimit` become `0`, and boolean flags like `superSeeding`/`forceStart`/`sequentialDownload` reset to `false` because `ApplyUserFields` always assigns them from the update model.

### Code pointers
- `src/NzbDrone.Core/Torrents/Torrent.cs` (`ApplyUserFields`, lines ~169-176)
- `src/Seedarr.Api.V1/Torrents/TorrentController.cs` (`Update`, ~1585-1628)
- Related: #847 fixed accidental stop on partial PUT but not numeric/boolean field clearing.

### Suggested fix
Use nullable/patch semantics for numeric and boolean user fields, or merge from `existing` in `ToModel` when properties are omitted (JSON patch or separate update DTO).
