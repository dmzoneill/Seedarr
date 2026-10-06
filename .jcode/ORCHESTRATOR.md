# Orchestrator (Jcode)

Two modes for [dmzoneill/Seedarr](https://github.com/dmzoneill/Seedarr):

| Mode | Workers do |
|------|------------|
| **fix** | Validate GitHub issues, patch + tests, push `main`, close issue |
| **bug-hunt** | Scan C# backend, dedupe against GitHub, file or reopen issues |

**Active mode:** `bug-hunt` (see `BUG_HUNT_PLAYBOOK.md`).

## Bug-hunt loop

1. Partition backend (`NzbDrone.Core`, `Seedarr.Api.V1`, `Seedarr.Http`, `NzbDrone.Host`+`Common`, `SignalR`).
2. Spawn headless hunters; **never leave slots idle** for more than one monitor cycle.
3. On `report`, assign the next area or deeper pass on the same tree.
4. Monitor (~60s): `swarm list`, restart idle/stopped workers, log to `monitor-log.jsonl`.

## Fix-mode loop (legacy)

Non-overlapping issue slots, worktrees `../seedarr_worktrees/issue-<N>`, continuous dispatch until queue empty.

## Constraints

- No local `dotnet build` / test runs.
- Simulation semantics apply to severity and tests.

State: `orchestrator-state.json`
