# AGENTS.md

Instructions for agents (and humans) working in this repository.

## Keep documentation in sync

Documentation is part of the deliverable. **Any** code change must update the relevant docs in
the same commit:

| Change | Update |
| --- | --- |
| Design/architecture decision | [`docs/design.md`](docs/design.md), and an ADR-style doc if it is significant |
| New or completed work | [`docs/implementation-plan.md`](docs/implementation-plan.md) — check off tasks, add new ones |
| Behavior difference vs. `aurora-dsql-orms` | [`docs/comparison-aurora-dsql-orms.md`](docs/comparison-aurora-dsql-orms.md) |
| Connector usage/decision | [`docs/connector-amazon-auroradsql-npgsql.md`](docs/connector-amazon-auroradsql-npgsql.md) |
| Test setup or categories | [`docs/testing-with-dsql-emulator.md`](docs/testing-with-dsql-emulator.md) |
| Emulator bug or limitation hit | [`docs/dsql-emulator-issues.md`](docs/dsql-emulator-issues.md) |
| Public API / usage | [`README.md`](README.md) |

Use `file:line` references when pointing at code. Do not let docs drift from behavior; if a
decision changes, change the doc, do not append a contradicting note.

## Work incrementally

- Take **one plan item at a time**. Keep each change small and self-contained.
- After each item: build, run tests, then commit and push.
- Commit messages: imperative mood, focused on the one item; reference the plan item when useful.
- Do not batch unrelated changes into one commit.
- When something new is discovered, **add it to `docs/implementation-plan.md`** (as a task or an
  open question) rather than silently fixing it.

## Engineering constraints

- Generate DSQL-correct SQL at SQL-generation time. Never wrap `DbConnection`/`DbCommand`, never
  regex over SQL, never shell out to an external tool. See [`docs/design.md`](docs/design.md) §3–4.
- Preserve `NpgsqlConnection`/`NpgsqlCommand` identity: pass the connector's underlying
  `NpgsqlDataSource` straight to `UseNpgsql`.
- Prefer failing loudly at model/SQL-generation time over emitting SQL that fails at the server.
- Add tests at the lowest layer that can prove correctness (unit SQL snapshots before integration).

## Target versions

- `net10.0`
- `Microsoft.EntityFrameworkCore.*` 10.0.x
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x
- `Amazon.AuroraDsql.Npgsql` 1.x

Record any change to these in [`docs/design.md`](docs/design.md) and in this file.

## Testing

- Unit tests must not require a database.
- Integration tests use the `dsql-emulator` container via Testcontainers.
  See [`docs/testing-with-dsql-emulator.md`](docs/testing-with-dsql-emulator.md).
- Never assert on server version, error message text, or `sys.jobs` id format — use SQLSTATE.
- If a test fails, classify it: our bug, emulator limitation (log it), or emulator bug
  (log it, reproduce minimally, and it is acceptable to fix and commit upstream in
  `Dreamescaper/dsql-emulator`).

## Before committing

```bash
dotnet build
dotnet test
```

Only commit when both succeed. Pushing is expected as part of the incremental workflow for this
repository.
