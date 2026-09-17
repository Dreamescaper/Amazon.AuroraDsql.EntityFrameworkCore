# dsql-emulator: issues and limitations

Tracks problems found while using
[`Dreamescaper/dsql-emulator`](https://github.com/Dreamescaper/dsql-emulator) for integration tests.

**Status:** pinned to `ghcr.io/dreamescaper/dsql-emulator:0.1.1`. Release `v0.2.0` cannot be adopted
yet — see the regression below.

## How to use this file

When a test fails against the emulator, first decide whether it is:

1. **Our bug** — fix the provider.
2. **Emulator limitation** — work around it, move the assertion to the live suite, and add an entry
   below.
3. **Emulator bug** — reproduce minimally, add an entry below, and open an issue upstream.

Entry template:

```markdown
### <short title>
- **Date:** YYYY-MM-DD
- **Emulator image:** ghcr.io/dreamescaper/dsql-emulator:<tag>
- **Type:** limitation | bug | accepted divergence
- **Impact on us:** <which test / feature>
- **Repro:** <minimal SQL / C#>
- **Workaround:** <what we do instead>
- **Upstream:** <issue link, if filed>
```

## Blocking: v0.2.0 regression breaks the wire protocol for Npgsql

- **Date:** 2026-09-17
- **Emulator image:** ghcr.io/dreamescaper/dsql-emulator:0.2.0
- **Type:** bug (regression vs `v0.1.1`)
- **Impact on us:** the whole integration suite (9/9) and the harness `FindNpgsqlTest` (411/411)
  fail on a fresh `v0.2.0` with
  `Npgsql.NpgsqlException : Received backend message CommandComplete while expecting ParseCompleteMessage`.
  The same suites pass on a fresh `v0.1.1`.
- **Repro (Npgsql extended protocol):**
  ```csharp
  await using var conn = new NpgsqlConnection(
      "Host=127.0.0.1;Port=55432;Username=admin;Password=x;Database=postgres;SSL Mode=Require");
  await conn.OpenAsync();
  await using var tx = await conn.BeginTransactionAsync();
  await using var cmd = conn.CreateCommand();
  cmd.CommandText = "SELECT 1 FROM \"NoSuchTableXYZ\"";
  await cmd.ExecuteReaderAsync();   // v0.2.0: protocol error; v0.1.1: PostgresException (0A000)
  ```
  At the EF level it reproduces on a fresh database in
  `HistoryRepository.GetAppliedMigrationsAsync` (the history table does not exist yet), i.e. any
  backend error raised inside a transaction.
- **Suspected area:** the new commit-time OCC machinery (`internal/proxy/adjudicator.go`,
  `session.occIntercept` / `startRepair` / `reject` with `extended=true`) mishandles the message
  sequence when a statement inside a transaction is refused or errors: the client receives a
  `CommandComplete` where the extended protocol requires `ParseComplete`.
- **Workaround:** pin `v0.1.1` (current).
- **Upstream:** https://github.com/Dreamescaper/dsql-emulator/issues/1

## What v0.2.0 changes (for when it is adoptable)

- `CREATE INDEX ASYNC` is now rewritten in **multi-statement** queries too (scanner-based, not a
  regex) — the old "whole statements only" limitation is gone.
- OCC conflicts are **adjudicated at COMMIT without waiting for locks**, across write-write,
  `FOR UPDATE`, `FOR KEY SHARE` and foreign-key overlap; the loser is decided at row access rather
  than commit order (documented divergence remains).
- Refusal **wording now mirrors Aurora DSQL exactly**.
- Deterministic `occ.inject` rules are validated, but still not exposed by any CLI flag/env (see the
  open item below).

## Known limitations relevant to our tests

Source: emulator README "What it does not do" (as of the pinned `v0.1.1`). These are documented
behavior, not new findings.

### Locking semantics differ (blocking vs lock-free)
- **Type:** limitation
- **Impact:** Do not assert on conflict *timing* or on `FOR UPDATE`/lock-wait behavior. DSQL fails
  the second committer immediately; the emulator (backed by PostgreSQL) waits, then fails.
- **Workaround:** Use deterministic conflict injection for OCC retry tests; assert only the
  outcome (`40001`), never the timing.

### IAM tokens accepted but not validated
- **Type:** limitation
- **Impact:** Any password connects; the token is never checked. Separately, the
  `Amazon.AuroraDsql.Npgsql` connector cannot be used here anyway, because it forces
  `SslMode=VerifyFull` (which rejects the emulator's self-signed certificate) and requires real
  AWS credentials/region to mint a token.
- **Workaround:** Integration tests create a plain `NpgsqlDataSource` (not `Amazon.AuroraDsql.Npgsql`)
  with `SSL Mode=Require` and a dummy password. IAM auth is covered by the live suite only.

### `ASYNC` rewrite matches whole statements (fixed in v0.2.0, not yet adopted)
- **Type:** limitation (pinned version)
- **Impact:** A **multi-statement** simple query containing `CREATE INDEX ASYNC` is not rewritten,
  so PostgreSQL rejects it with a syntax error where DSQL reports its own error.
- **Workaround:** Ensure the provider and tests send one statement per command (our
  `IMigrationCommandExecutor` does). `v0.2.0` removes this limitation once the regression above is
  fixed.

### `sys.jobs` is partial
- **Type:** limitation
- **Impact:** Only index builds and constraint validation are recorded. Job ids are UUIDs, whereas
  real DSQL ids are 26 characters. `CREATE INDEX ASYNC IF NOT EXISTS` on an existing index returns
  an id but records no row.
- **Workaround:** Avoid asserting on `sys.jobs` id format or row counts for `IF NOT EXISTS`
  idempotent re-runs.

### Version reporting leaks on unusual paths
- **Type:** limitation
- **Impact:** `version()`, `SHOW server_version`, `current_setting('server_version'...)` are
  rewritten; reads via any other path report the backing PostgreSQL version.
- **Workaround:** Do not assert on server version.

### Rejection wording is approximate (fixed in v0.2.0, not yet adopted)
- **Type:** limitation (pinned version)
- **Impact:** Error messages mirror DSQL's meaning but drift.
- **Workaround:** Assert on **SQLSTATE**, never on message text. `v0.2.0` mirrors the wording
  exactly once the regression above is fixed.

### No control plane
- **Type:** limitation
- **Impact:** Cluster creation, IAM and tagging are out of scope (LocalStack covers control-plane
  APIs). Nothing for us to test here beyond connecting to an existing emulator instance.

### Backing database needs init scripts
- **Type:** limitation
- **Impact:** The row-cap trigger, the `sys` schema and the `admin` role come from `docker/init/`.
  A hand-rolled PostgreSQL will not enforce the 3,000-row cap or accept `admin`.
- **Workaround:** Always use the published image or `docker-compose.yml`; never a bare PostgreSQL.

## Open items / requested upstream

### Deterministic OCC conflict injection is not reachable from the container
- **Date:** 2026-09-17
- **Emulator image:** ghcr.io/dreamescaper/dsql-emulator:0.1.1
- **Type:** limitation (blocked test), candidate upstream change
- **Detail:** the emulator supports deterministic commit-conflict injection, but the rules come from
  the embedded ruleset (`rules.Default()`; `session.occCommitConflict` reads
  `classifier.Ruleset().OCC.Inject`, default `[]`). Neither `cmd/dsql-emu` nor the container exposes a
  flag/env to supply a custom ruleset — unchanged in `v0.2.0` (which adds validation and tests for
  `occ.inject` rules but no way to load them). So an induced `40001` cannot be triggered from
  integration tests.
- **Impact on us:** the "induced OCC conflict is retried" integration test is blocked; OCC
  classification and retry are covered by unit tests instead.
- **Candidate fix (upstream, `Dreamescaper/dsql-emulator`):** add a `--rules <file>` flag (and
  `DSQL_EMU_RULES` env) that loads a ruleset YAML and passes it via `proxy.Config.Ruleset`. A new
  image release would then be pinned here and the test enabled.
- **Upstream:** not filed yet.

## Findings log

### Explicit isolation level rejected (`0A000: Unsupported isolation level: READ COMMITTED`)
- **Date:** 2026-09-17
- **Emulator image:** ghcr.io/dreamescaper/dsql-emulator:0.1.1
- **Type:** expected behaviour (validates DSQL semantics), surfaced by our first integration run
- **Impact on us:** Npgsql maps `IsolationLevel.Unspecified` to an explicit `READ COMMITTED`;
  every `BeginTransaction` failed.
- **Fix (ours):** `DsqlRelationalConnection` forces `IsolationLevel.RepeatableRead`.
- **Upstream:** none; emulator is correct.

### One DDL per transaction enforced (`0A000: a transaction can include only one DDL statement`)
- **Date:** 2026-09-17
- **Emulator image:** ghcr.io/dreamescaper/dsql-emulator:0.1.1
- **Type:** expected behaviour (validates DSQL semantics)
- **Impact on us:** EF's `Migrator` opens one transaction for the whole migration, so applying a
  multi-statement migration failed.
- **Fix (ours):** `DsqlMigrationCommandExecutor` commits and drops the migrator's transaction and
  lets each statement autocommit.
- **Upstream:** none; emulator is correct.
