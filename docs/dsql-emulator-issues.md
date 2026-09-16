# dsql-emulator: issues and limitations

Tracks problems found while using
[`Dreamescaper/dsql-emulator`](https://github.com/Dreamescaper/dsql-emulator) for integration tests.

**Status:** no provider-specific findings yet — implementation has not started. The entries below
are the emulator's documented limitations (from its README) that are relevant to our test strategy,
recorded so we do not mistake them for provider bugs or assert behavior the emulator does not have.

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

## Known limitations relevant to our tests

Source: emulator README "What it does not do". These are documented behavior, not new findings.

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

### `ASYNC` rewrite matches whole statements
- **Type:** limitation
- **Impact:** A **multi-statement** simple query containing `CREATE INDEX ASYNC` is not rewritten,
  so PostgreSQL rejects it with a syntax error where DSQL reports its own error.
- **Workaround:** Ensure the provider and tests send one statement per command (our
  `IMigrationCommandExecutor` does). Do not test multi-statement `psql -c 'a; b'` style batching
  against the emulator.

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

### Rejection wording is approximate
- **Type:** limitation
- **Impact:** Error messages mirror DSQL's meaning but drift.
- **Workaround:** Assert on **SQLSTATE**, never on message text.

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
  `classifier.Ruleset().OCC.Inject`). Neither `cmd/dsql-emu` nor the container exposes a flag/env to
  supply a custom ruleset, so an induced `40001` cannot be triggered from integration tests.
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
