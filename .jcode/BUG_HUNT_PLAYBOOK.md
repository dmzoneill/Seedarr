# Backend bug hunt (Seedarr simulation)

Workers scan **C# backend** for real defects. Seedarr is a **BitTorrent seeding simulator**: judge bugs against simulated I/O and test doubles, not production torrent clients.

## Before filing on GitHub (`dmzoneill/Seedarr`)

1. Search open **and** closed issues: `gh issue list --state all --search "<keywords>" --limit 30`
2. Read candidate issues **and comments** (`gh issue view N --comments`).
3. If same root cause: **reopen** if closed (`gh issue reopen N`) and add a comment with file:line, scenario, expected vs actual.
4. If no match: **open** a new issue with title `bug(<area>): <short>`, body with reproduction, impact, suggested fix, and code pointers.

Do **not** implement fixes in this mode unless the orchestrator assigns fix mode.

## No local CI

Do not run `dotnet build` or tests on the worker machine. GitHub Actions validates fixes later.

## Progress file (workers only)

- Update **`.jcode/hunt-progress.json`** for your slot: `currentSubfolder`, `lastPass` (subfolder, `issuesFiled`, `issuesReopened`, `completedAt`).
- **Never edit** `.jcode/orchestrator-state.json` (coordinator + monitor only). Race on that file breaks restarts and scheduling.
- After each subfolder pass, rotate to the next folder under your partition and record it in `hunt-progress.json`.

## Reporting

When a pass completes (or you are blocked), `swarm report` with: area scanned, issues filed/reopened (numbers), next subfolder (also written to `hunt-progress.json`).

## Coordinator

Keeps workers busy, restarts idle/stopped hunters, reads `hunt-progress.json` to avoid overlap.
