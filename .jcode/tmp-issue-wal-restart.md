## Summary

`WalCheckpointTask` treats any successful passive checkpoint with `Busy == 0` as a signal to run a RESTART checkpoint. On PostgreSQL, `MainDatabase.Checkpoint` returns `Success = true` with default `Busy = 0` without performing WAL work, so the scheduled task always issues a second RESTART call and logs misleading SQLite-specific success text.

## Reproduction

1. Configure Seedarr with PostgreSQL (`PostgresHost` set).
2. Wait for the 15-minute `WalCheckpointTask` schedule or trigger `WalCheckpointCommand`.
3. Observe logs: passive success message followed by RESTART checkpoint invocation.

## Expected

Non-SQLite engines skip WAL checkpoint escalation entirely (or report that WAL is N/A once).

## Actual

PostgreSQL runs two checkpoint calls per schedule tick; debug log claims passive WAL succeeded and resets the WAL write pointer.

## Impact

Noise in logs/metrics and unnecessary maintenance work on PostgreSQL; operators may misread WAL health during simulated seeding runs.

## Suggested fix

Gate the RESTART branch on `DatabaseType.SQLite` (or require `WalLogPages > 0`).

## Code pointers

- `src/NzbDrone.Core/Datastore/WalCheckpointTask.cs` (`Execute`, lines 20-29)
- `src/NzbDrone.Core/Datastore/MainDatabase.cs` (`Checkpoint`, PostgreSQL early return lines 90-98)

## Dedupe

Searched issues for WalCheckpointTask Restart PostgreSQL; no match.
