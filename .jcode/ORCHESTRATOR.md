# Orchestrator (Jcode)

Two modes for [dmzoneill/Seedarr](https://github.com/dmzoneill/Seedarr):

| Mode | Workers do |
|------|------------|
| **fix** | Validate GitHub issues, patch + tests, push `main`, close issue |
| **bug-hunt** | Scan C# backend, dedupe against GitHub, file or reopen issues |

**Active mode:** `fix` (see `FIX_PLAYBOOK.md`). Bug-hunt: `BUG_HUNT_PLAYBOOK.md`.

## Fix-mode loop

1. Pick non-conflicting open GitHub issues (one per component / path).
2. Create worktree `../seedarr_worktrees/issue-<N>`, spawn headless fix agent per slot.
3. Agents validate issue + comments; fix or close as invalid/duplicate.
4. Push `origin/main`, close issue, `swarm report` to coordinator.
5. Monitor (~120s): `swarm list`, poke idle agents, dispatch next issue when slot completes.

## Bug-hunt loop

1. Partition backend (`NzbDrone.Core`, `Seedarr.Api.V1`, `Seedarr.Http`, `NzbDrone.Host`+`Common`, `SignalR`).
2. Spawn headless hunters; **never leave slots idle** for more than one monitor cycle.
3. On `report`, assign the next area or deeper pass on the same tree.
4. Monitor (~60s): `swarm list`, restart idle/stopped workers, log to `monitor-log.jsonl`.

## Constraints

- No local `dotnet build` / test runs.
- Simulation semantics apply to severity and tests.

State: `orchestrator-state.json` (coordinator only). Hunter rotation: `hunt-progress.json`.
