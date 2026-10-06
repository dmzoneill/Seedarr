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
