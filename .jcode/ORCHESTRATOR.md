# Issue orchestrator (Jcode)

Coordinates swarm workers on **non-overlapping file slots** for [dmzoneill/Seedarr](https://github.com/dmzoneill/Seedarr).

## Loop

1. Pick open issues that do not share hot files.
2. Spawn a worker per slot with a dedicated git worktree under `../seedarr_worktrees/issue-<N>`.
3. Worker validates the issue, implements fix + tests, pushes `main`, closes the issue.
4. On worker `report`, immediately assign the next queued issue to that slot (never leave workers idle).
5. When the queue is empty, poll GitHub for new open issues and refill slots.

## Constraints

- No local `dotnet build` / test runs; GitHub Actions validates.
- Treat Seedarr as a **seeding simulator** when judging severity and test expectations.

State: `orchestrator-state.json`

## Monitor (30s, optional)

- Coordinator wakes on `schedule` with `wake_at` ~30s ahead (`wake_in_minutes` minimum is 1).
- Each cycle: `swarm list`, `gh issue list --state open`, reconcile `orchestrator-state.json`, dispatch idle slots, stop stale workers on closed issues.
- Re-schedule the next cycle at the start of each wake **only while there are open issues or queued work**.
- When GitHub has **0 open issues** and the queue is empty, cancel pending monitor schedules and set `status: stopped` in state.
- Optional log: `.jcode/monitor-log.jsonl`.
