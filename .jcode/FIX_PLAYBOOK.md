# Fix-mode playbook

Assign GitHub issues to swarm fix agents. Seedarr is a **BitTorrent seeding simulator**; judge severity and tests against simulated I/O semantics, not production torrent client guarantees.

## Agent workflow (per issue)

1. Work only in `../seedarr_worktrees/issue-<N>` (branch `fix/issue-<N>`).
2. `git fetch origin main` and rebase onto `origin/main` before coding.
3. Read GitHub issue **#N** and **all comments** (`gh issue view N --comments`).
4. **Validate:** challenge the report. If invalid, duplicate, or out of scope for simulation, comment on GitHub with reasoning and **close** without code changes.
5. If valid: implement minimal fix + update/add tests. **Do not** run `dotnet build` or `dotnet test` locally.
6. Commit, `git push origin HEAD:main` (fast-forward main). Resolve push races by rebasing on latest `origin/main` and retry once.
7. Close issue with a short comment referencing the commit.
8. `swarm` action `report` to coordinator: outcome (fixed | invalid | duplicate), issue #, commit sha.

## Non-conflict rules (orchestrator)

- At most one active issue per overlapping path prefix (same file or same API controller folder).
- Prefer one open issue per component tag in the title (`authentication`, `host`, `downloadclients`, `torrents`, `terminal`, etc.).
- Do not assign two issues that both touch `DownloadClients` controllers in the same cycle.

## Slots (default 5)

| Slot | Typical areas |
|------|----------------|
| core | `src/NzbDrone.Core` |
| api | `src/Seedarr.Api.V1` |
| http | `src/Seedarr.Http` |
| host | `src/NzbDrone.Host`, `src/NzbDrone.Common` |
| signalr | `src/NzbDrone.SignalR` |

## Monitor (120s)

See scheduled task `ORCHESTRATOR FIX MONITOR` in coordinator session.
