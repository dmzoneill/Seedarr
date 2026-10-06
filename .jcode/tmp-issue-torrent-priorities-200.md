### Summary
Bulk torrent file priority update returns HTTP 200 with `{ "success": false }` when one or more file IDs are invalid, with no per-file error detail.

### Reproduction
1. Pick a torrent with known file ids.
2. `PUT /api/v1/torrents/{torrentId}/files/priorities` with a mix of valid and invalid `fileId` values.

### Expected
`404` or `400` when any file id does not belong to the torrent, or a structured partial-failure payload with HTTP 207/409.

### Actual
`200 OK` and `{ "success": false }`. `TorrentFileService.SetPriorities` sets `allOk = false` but the controller always returns `Ok(...)`.

### Code pointers
- `src/Seedarr.Api.V1/Torrents/TorrentController.cs` (`SetFilePriorities`, ~572-584)
- `src/NzbDrone.Core/Torrents/TorrentFileService.cs` (`SetPriorities`, ~87-104)
- Related: #898 (RBAC on priority endpoints) is separate.

### Suggested fix
Return `404`/`400` on failure, or include failed file ids in the response and use a non-2xx status when `success` is false.
