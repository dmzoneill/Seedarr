### Summary
`PUT /api/v1/torrents/{id}/queue` returns HTTP 200 even when the requested queue position token is invalid, and the torrent order is left unchanged.

### Reproduction
1. Note current queue order for torrent id `N`.
2. `PUT /api/v1/torrents/N/queue` with body `{ "position": "not-a-valid-position" }`.
3. List torrents / inspect `sortOrder`.

### Expected
`400 Bad Request` (or similar) describing allowed values: `top`, `up`, `down`, `bottom`.

### Actual
`200 OK` with empty body. `TorrentService.MoveQueue` hits the `default` branch, re-inserts at the original index, and returns without updating sort orders.

### Code pointers
- `src/Seedarr.Api.V1/Torrents/TorrentController.cs` (`MoveQueue`, ~1750-1762)
- `src/NzbDrone.Core/Torrents/TorrentService.cs` (`MoveQueue`, `default` branch ~684-686)
- Related open issue: #942 (null body NRE) is a separate failure mode.

### Suggested fix
Validate `position` in the controller (or return a boolean from `MoveQueue`) and respond with `400` when the token is unknown.
