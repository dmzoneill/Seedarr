### Summary
`POST /api/v1/downloadhistory/enrich-all` returns `{ "message": "Enrichment started" }` even when `IArrMetadataEnricherService` is not registered, so clients believe background enrichment was kicked off.

### Reproduction
1. Host Seedarr without `IArrMetadataEnricherService` wired (optional ctor parameter on `DownloadHistoryController`).
2. `POST /api/v1/downloadhistory/enrich-all`.

### Expected
`400`/`503` indicating metadata enrichment is unavailable (similar to `POST .../enrich` on a single entry).

### Actual
`200 OK` with `{ "message": "Enrichment started" }` because the action only schedules work inside `if (_metadataEnricherService != null)` but always returns Ok.

### Code pointers
- `src/Seedarr.Api.V1/Torrents/DownloadHistoryController.cs` (`EnrichAll`, ~132-151)
- Related: #896 (RBAC) and closed #705 (background execution) do not cover the false-success path.

### Suggested fix
If `_metadataEnricherService` is null, return `400` with the same message used by single-entry enrich.
