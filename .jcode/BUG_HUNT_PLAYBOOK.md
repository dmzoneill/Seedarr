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

- Each hunt slot has its **own** file under `.jcode/hunt-progress/`:
  - core → `core.json`
  - api → `api.json`
  - http → `http.json`
  - host → `host.json`
  - signalr → `signalr.json`
- Update **only your slot file**. Do **not** overwrite `.jcode/hunt-progress.json` (manifest only) or other slot files.
- Fields: `currentSubfolder`, `lastPass` (subfolder, `issuesFiled`, `issuesReopened`, `completedAt`), optional `rotation` array.
- **Never edit** `.jcode/orchestrator-state.json` (coordinator + monitor only).
- After each subfolder pass, rotate to the next folder under your partition and record it in your slot file.

## Reporting

When a pass completes (or you are blocked), `swarm report` with: area scanned, issues filed/reopened (numbers), next subfolder (also written to your slot JSON).

## Coordinator

Keeps workers busy, restarts idle/stopped hunters, reads `.jcode/hunt-progress/*.json` to avoid overlap.
