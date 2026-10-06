## Summary

`BasicRepository.UpdateMany` does not guard a null `models` argument. `InsertMany` returns early on null, but `UpdateMany` calls `models.ToList()` and throws `ArgumentNullException`, breaking callers that treat both batch APIs symmetrically.

## Reproduction

1. Invoke `repository.UpdateMany(null)` on any `BasicRepository<T>` (e.g. during a simulated bulk update path).
2. Observe `ArgumentNullException` before any database work.

## Expected

Null input is ignored (no-op), matching `InsertMany`.

## Actual

`ArgumentNullException` from `Enumerable.ToList()`.

## Impact

Defensive callers or test doubles can crash persistence layers during simulation.

## Suggested fix

Add `if (models == null) return;` before materializing the list (mirror `InsertMany`).

## Code pointers

- `src/NzbDrone.Core/Datastore/BasicRepository.cs` (`UpdateMany`, lines 165-168)

## Dedupe

Searched issues for UpdateMany null BasicRepository; no match.
