## Summary

`ConnectionStringFactory.BuildPostgresConnectionString` concatenates config fields into a raw ADO.NET connection string. Passwords or usernames containing `;`, `=`, or `\` are not escaped, so Npgsql parses them incorrectly and startup/migrations fail for otherwise valid credentials.

## Reproduction

1. Set `PostgresHost` and other Postgres settings in `config.xml`.
2. Use a password that includes a semicolon (common when generated).
3. Start Seedarr (simulated or real Postgres).

## Expected

Connection string is built safely for arbitrary credential characters.

## Actual

Connection string is truncated or mis-parsed; database creation fails before migrations run.

## Impact

PostgreSQL deployments cannot start when credentials contain delimiter characters.

## Suggested fix

Build the string with `NpgsqlConnectionStringBuilder` (same pattern as `DbFactory.RedactConnectionString`).

## Code pointers

- `src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs` (`BuildPostgresConnectionString`, lines 42-48)

## Dedupe

Searched open/closed issues for Postgres password / ConnectionStringFactory escaping; no match.
