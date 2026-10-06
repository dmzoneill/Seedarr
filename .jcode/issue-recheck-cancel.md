## Summary

`TorrentRecheckService.CancelRecheck` removes a torrent id from `_queuedTorrentIds` but does not reset torrent status when the recheck was only queued (not yet active). The queue processor dequeues the id, sees it is no longer in `_queuedTorrentIds`, and skips execution while the torrent remains in `QueuedForChecking` indefinitely.

## Reproduction (simulated I/O)

1. Queue a recheck via `QueueRecheck(torrentId)` (status becomes `QueuedForChecking`).
2. Before `ProcessQueueAsync` starts the job, call `CancelRecheck(torrentId)`.
3. Observe torrent status stays `QueuedForChecking` with `Active=false` and zero speeds; no transition back to `Downloading`/`Paused`/`Seeding`.

## Expected

Cancelled queued recheck should restore the prior status (or a safe default like `Paused`/`Stopped`) and clear queue bookkeeping.

## Actual

Status remains `QueuedForChecking` forever; UI shows perpetual "queued for checking".

## Code pointers

- `src/NzbDrone.Core/Torrents/TorrentRecheckService.cs`
  - `QueueRecheck` sets `TorrentStatus.QueuedForChecking` (~lines 86-93)
  - `CancelRecheck` only clears `_queuedTorrentIds` / cancels active CTS (~lines 116-130)
  - `ProcessQueueAsync` skips dequeued ids when `_queuedTorrentIds.TryRemove` fails (~lines 142-147)

## Suggested fix

On cancel of a queued (non-active) recheck: capture previous status before queueing (or revert to last non-checking status), update repository, publish `TorrentStatusChangedEvent`. Optionally drain/reconcile `_recheckQueue` entries for the id.

## Impact

Operators cannot reliably cancel a queued hash check; torrent appears stuck and will not resume simulated download/seeding until manual status change.
