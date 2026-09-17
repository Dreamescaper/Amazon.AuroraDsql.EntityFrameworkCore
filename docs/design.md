# Design

## 1. Background

**Amazon Aurora DSQL** is a distributed, serverless SQL database that speaks the PostgreSQL
wire protocol. It is compatible with `Npgsql` at the driver level, but supports only a subset
of PostgreSQL:

- A single database named `postgres`; logical separation via schemas or separate clusters.
- Only the `C` collation, and `UTC` as the system timezone.
- Fixed `Repeatable Read` isolation with **optimistic concurrency control (OCC)**; conflicts
  surface as `SQLSTATE 40001` and must be retried by the application.
- DDL and DML require separate transactions; **one DDL statement per transaction**; a
  transaction can modify at most 3,000 rows.
- `CREATE INDEX ASYNC` instead of `CREATE INDEX`; `DELETE` instead of `TRUNCATE`.
- No `CREATE TYPE`, no extensions (e.g. `uuid-ossp`, PostGIS), no procedural languages.
- Limited data types (see §5).
- Foreign keys are supported, but added constraints must use `NOT VALID` followed by
  `ALTER TABLE ASYNC ... VALIDATE CONSTRAINT`.

`Npgsql.EntityFrameworkCore.PostgreSQL` (`efcore.pg`) targets full PostgreSQL, so some of the
SQL it generates and some of its models are invalid on DSQL.

## 2. Scope

Provide an EF Core provider package, `Amazon.AuroraDsql.EntityFrameworkCore`, that reuses
`efcore.pg` and adapts it to DSQL so that `DbContext`, LINQ queries, migrations and
`SaveChanges` work without user-side SQL rewriting.

The provider does **not** reimplement query translation: for the large PostgreSQL-compatible
subset, `efcore.pg`'s query pipeline is already correct.

## 3. Key design decision: generate correct SQL, do not post-process it

The central decision is to fix DSQL incompatibilities **where the SQL is generated**, by
replacing a small set of EF Core/Npgsql services, rather than intercepting the ADO.NET
surface and rewriting or dropping SQL text.

Every observed DSQL incompatibility maps to a specific, public, overridable component:

| DSQL constraint | Emitter | Override |
| --- | --- | --- |
| `SAVEPOINT` emitted for `SaveChanges` inside a transaction | EF Core `BatchExecutor` (guarded by `transaction.SupportsSavepoints && Database.AutoSavepointsEnabled`) | `IRelationalTransactionFactory` returning a `RelationalTransaction` with `SupportsSavepoints => false` |
| `LOCK TABLE ... IN ACCESS EXCLUSIVE MODE` on `__EFMigrationsHistory` | `NpgsqlHistoryRepository` (`GetCreateScript`/`GetInsertScript`) | custom `IHistoryRepository` subclass without the lock |
| `SET TRANSACTION ISOLATION LEVEL` | `NpgsqlRelationalConnection.ConnectionBeginTransaction` | subclass; force `IsolationLevel.Unspecified` (only Repeatable Read is meaningful) |
| `CREATE INDEX` | `NpgsqlMigrationsSqlGenerator.Generate(CreateIndexOperation, ...)` | emit `CREATE INDEX ASYNC` |
| FK on existing table | `NpgsqlMigrationsSqlGenerator.ForeignKeyConstraint(...)` | emit `NOT VALID`, then `ALTER TABLE ASYNC ... VALIDATE CONSTRAINT` |
| Multiple DDL statements in one migration transaction | EF `IMigrationCommandExecutor` | per-command transaction executor |
| OCC conflict retry | EF `IExecutionStrategy` | DSQL retry strategy with backoff/jitter on `40001` |
| Unsupported types/features | model validation + `IRelationalTypeMappingSource` | model validator + trimmed type mapping source |
| Stored primitive collections → native PG arrays (unstorable) | `NpgsqlTypeMappingSource.FindCollectionMapping` | subclass; map stored collections to `jsonb`, keep parameters as arrays (§5.1) |
| UUID keys need a server-side default | model conventions | model-finalizing convention: `gen_random_uuid()` |
| Identity columns require an explicit cache | migrations SQL generator | always emit `CACHE 1` (or `CACHE n` when `EnableIdentityColumns`); model annotations do not survive to the runtime model |

### Benefits

- **Deterministic.** No dependence on the exact text EF happens to emit; we control it.
- **Testable.** Generated SQL can be asserted as strings in unit tests, no database needed.
- **Type identity preserved.** Connections/commands stay `NpgsqlConnection`/`NpgsqlCommand`,
  so Npgsql features (binary import/export, `DbBatch`, notifications, `GetSchema`,
  `NpgsqlCommandBuilder`) and third-party code that casts continue to work.
- **No runtime overhead.** No per-command inspection on the hot path.
- **No external process.** Does not require bundling or discovering a native `dsql-lint` binary.
- **Fails loudly.** Unsupported operations throw at model/SQL-generation time with actionable
  messages, instead of generating SQL that fails at the server (possibly mid-migration).

## 4. Rejected alternative: ADO.NET interception + SQL rewriting

The [awslabs/aurora-dsql-orms](https://github.com/awslabs/aurora-dsql-orms) adapter takes a
different approach:

1. `DsqlWrappingDataSource` wraps `NpgsqlDataSource` and returns a `DsqlConnectionWrapper`
   (a `DbConnection`, not `NpgsqlConnection`).
2. `DsqlConnectionWrapper` returns `DsqlCommandWrapper` (a `DbCommand`) for every command and
   manually issues `BEGIN`, returning a hand-rolled `DsqlTransactionWrapper`.
3. `DsqlCommandWrapper` regex-sniffs every command's text and **suppresses**
   `SET TRANSACTION ISOLATION LEVEL`, `SAVEPOINT` and `LOCK TABLE` by returning `0`/`null`.
4. `DsqlMigrator` overrides `Migrator.GenerateUpSql`/`GenerateDownSql`, shells out to the
   external `dsql-lint` binary, splits the script on `;`, regex-injects `IF NOT EXISTS`, and
   rebuilds every `MigrationCommand` with `transactionSuppressed: true`.

This works, but is fragile for our purposes:

- **Type-identity loss.** Wrapping `DbConnection`/`DbCommand` breaks any code that casts to
  the Npgsql types. The adapter already had to replace `IRelationalDatabaseCreator` because
  `NpgsqlDatabaseCreator.Exists()` casts the connection.
- **Transaction model bypassed.** Manually sending `BEGIN` and wrapping `DbTransaction` breaks
  `NpgsqlTransaction` casts, savepoint semantics and transaction enlistment.
- **Regex is ambiguous.** Comments, leading whitespace, `SET LOCAL`, casing and string literals
  can defeat or falsely trigger the sniffer; suppression silently reports success, masking errors.
- **Migration rewriting is lossy.** External native binary per migration, JSON schema-version
  coupling, naive `;` splitting (breaks on semicolons inside string literals, dollar-quoted
  function bodies incl. `CREATE FUNCTION ... LANGUAGE SQL`, and check constraints), and loss of
  EF's `MigrationCommand` metadata.
- **Internal APIs.** Uses `#pragma warning disable EF1001`.

We keep what is genuinely good in that adapter (the model conventions for UUID/identity keys
and the OCC execution strategy) and discard the interception/rewriting parts.

## 5. Data type mapping constraints

DSQL storable types (per the AWS docs): `smallint`, `integer`, `bigint`, `real`,
`double precision`, `numeric(p,s)` (default `numeric(18,6)`, max precision 1000), `character`,
`varchar` (max 65535 bytes), `text` (max 1 MiB), `date`, `time`, `timetz` (not indexable),
`timestamp`, `timestamptz`, `interval`, `boolean`, `bytea`, `uuid`, `json`, `jsonb`.

PostgreSQL array types and `inet` are **query-runtime only** and cannot be stored in columns.
Primitive collections are therefore mapped to `jsonb` instead — see §5.1.

Implications:

- Model properties mapped to ranges, `hstore`, `tsvector`, geometry, enums or custom composite
  types must be rejected by the model validator with a clear message.
- `decimal` should default to a DSQL-compatible precision/scale; `numeric(18,6)` is the
  server default when unspecified — align the model convention with it.
- `timetz` and `bytea` are not indexable; the validator should warn if an index is defined on
  such a column.

## 5.1 Primitive collections and arrays → `jsonb`

Aurora DSQL cannot store PostgreSQL array types, but it fully supports `json`/`jsonb` and all
PostgreSQL JSON functions and operators. Primitive collections (`int[]`, `List<string>`, …) are
therefore mapped to a **`jsonb` column containing a JSON array**, the same way EF Core maps
primitive collections to JSON on SQL Server rather than to a SQL Server-native type.

### Type mapping

`NpgsqlTypeMappingSource` normally maps collections to `NpgsqlArrayTypeMapping` (a native PG
array). Subclass it as `DsqlTypeMappingSource` and override
`FindCollectionMapping(storeType, modelClrType, providerClrType, elementMapping)`:

- If a store type was explicitly specified, honor it.
- Otherwise, for a supported primitive collection, call
  `TypeMappingSourceBase.TryFindJsonCollectionMapping(...)` to obtain the element comparer and
  the `JsonValueReaderWriter`, then request the `jsonb` mapping and attach
  `CollectionToJsonStringConverter<TElement>` via `WithComposedConverter(..., elementMapping,
  collectionReaderWriter)`. This mirrors `SqlServerTypeMappingSource.FindCollectionMapping`.
- `byte[]` continues to map to `bytea` and is never treated as a collection.
- `Dictionary<,>` is excluded (EF Core already excludes it).

### Query translation is already provided by efcore.pg

`NpgsqlQueryableMethodTranslatingExpressionVisitor.TranslatePrimitiveCollection` already has a
branch for `NpgsqlJsonTypeMapping { ElementTypeMapping: not null }` that expands the array with
`jsonb_array_elements_text(...) WITH ORDINALITY` and casts each element back to its CLR type.
It exists today for scalar collections nested inside JSON documents; once the mapping above
routes top-level primitive collections to `NpgsqlJsonTypeMapping`, the same branch is used
automatically. **No custom query translator is required.**

Verified translations against DSQL-supported JSON operators:

- `collection.Contains(x)` → `"col" @> to_jsonb(x)` (containment, no unnesting);
- element queries (`Any`, element access) → `jsonb_array_elements_text("col") WITH ORDINALITY`.

For that branch to engage, the composed mapping must satisfy:

- `StoreType` is `jsonb` (or `json`; selectable with `.HasColumnType("json")`);
- `ElementTypeMapping` is non-null;
- the element mapping exposes a `JsonValueReaderWriter` — EF Core's built-in scalar mappings do,
  and we must verify each element type we allow.

### Storage and migration

- Column type is `jsonb`; DSQL stores `jsonb` up to ~1 MiB compressed.
- `jsonb` columns are **not indexable** in DSQL. `HasIndex` on a primitive collection is
  rejected by the model validator, and `Contains`/`Any` translate to a full scan.
- Converting an existing PG array column to `jsonb` is a data migration
  (`to_jsonb(col)` / `array_to_json(col)`), not an automatic column-type change.

### Semantics to document

- Values are stored as JSON, so element types are enforced by the provider's reader/writer, not
  by the database.
- `null` elements are represented as JSON `null`.
- Ordering and equality follow JSON semantics, not PostgreSQL array semantics.

## 6. Migrations

- **Per-statement transactions.** Replace `IMigrationCommandExecutor` so each generated command
  runs in its own transaction, satisfying "one DDL statement per transaction" and
  "DDL/DML in separate transactions".
- **Idempotency.** Emit `IF NOT EXISTS` / `IF EXISTS` **in the SQL generator**, per known
  operation type, so a migration that fails partway can be re-run safely. This is deterministic,
  unlike regex injection over concatenated SQL. EF's `__EFMigrationsHistory` remains the source
  of truth for applied migrations.
- **No `LOCK TABLE`.** Replace `IHistoryRepository` with a variant whose create/insert scripts
  omit the `ACCESS EXCLUSIVE` lock.
- **`CREATE INDEX ASYNC`.** Override `Generate(CreateIndexOperation, ...)`. Note DSQL has no
  `CONCURRENTLY`; users should not set the `CreatedConcurrently` annotation.
- **Foreign keys.** Emit `NOT VALID` when adding to an existing table and follow with
  `ALTER TABLE ASYNC <table> VALIDATE CONSTRAINT <name>`. FK cascading actions count toward the
  transaction row limit.

## 6.1 Idempotent migrations

DSQL runs one DDL statement per transaction and gives no cross-statement atomicity, so a migration
that fails partway leaves earlier objects in place while `__EFMigrationsHistory` records nothing.
Re-running the migration must therefore skip what already exists.

The provider emits `IF NOT EXISTS` / `IF EXISTS` **in the SQL generator**, per known
`MigrationOperation`, instead of post-processing the generated SQL:

| Operation | Emitted |
| --- | --- |
| `CreateTableOperation` | `CREATE TABLE IF NOT EXISTS` |
| `CreateIndexOperation` | `CREATE [UNIQUE] INDEX ASYNC IF NOT EXISTS` (name required by DSQL) |
| `AddColumnOperation` | `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` |
| `EnsureSchemaOperation` / `CreateSequenceOperation` | `CREATE SCHEMA`/`CREATE SEQUENCE IF NOT EXISTS` |
| `DropTable`/`DropIndex`/`DropColumn`/`DropConstraint`/`DropSchema`/`DropSequence` | `... IF EXISTS` |
| `AddForeignKeyOperation` / `AddCheckConstraintOperation` | `... NOT VALID` (no `IF NOT EXISTS` in PG) |

`ALTER TABLE ... ADD CONSTRAINT` has no `IF NOT EXISTS` form, so the provider's
`IMigrationCommandExecutor` additionally tolerates duplicate-object SQLSTATEs (`42710`, `42P07`,
`42701`) while applying a migration, logging and continuing — that makes constraint additions safe
to replay.

This is deliberately **not**: regex over SQL, splitting and rejoining scripts, or shelling out to a
tool. `IF NOT EXISTS` also does not compare definitions, so a half-created object with the wrong
shape is skipped; that is acceptable for a resumed migration and is documented.

### Comparison with `awslabs/aurora-dsql-orms`

The AWS adapter (`DsqlMigrator` → `DsqlSqlTransform` → `DsqlLintRunner`) shells out to the native
`dsql-lint` binary to fix DSQL syntax, then `DsqlSqlTransform.MakeIdempotent` **regexes** the
generated DDL to insert `IF NOT EXISTS` after `CREATE TABLE` and `CREATE [UNIQUE] INDEX [ASYNC]`.
It explicitly does not touch `ALTER`/`DROP` (their comment: those need manual handling if a
migration fails partway), and it joins statements with `;` and splits them back to lint in one
batch. Our approach covers more operations (`ADD COLUMN`, drops), avoids regex and an external
process, and keeps each `MigrationCommand` intact.

## 7. Transactions and concurrency

- DSQL has a single, fixed isolation level. Force `IsolationLevel.RepeatableRead` (Npgsql maps
  `Unspecified` to an explicit `READ COMMITTED`, which DSQL rejects) instead of sending arbitrary
  `SET TRANSACTION ISOLATION LEVEL`.
- Implement `IExecutionStrategy` that retries on `SQLSTATE 40001` with exponential backoff and
  jitter. `SaveChanges` inside an explicit transaction is not retried by EF, so provide an
  `ExecuteInTransactionAsync`-style helper that clears tracked state between attempts.
- Disable auto-savepoints (`SupportsSavepoints => false`), since DSQL does not support them.

## 8. Connections and authentication

Use `Amazon.AuroraDsql.Npgsql`'s `DsqlDataSource` for connections and IAM token auth. Its
underlying `NpgsqlDataSource` is passed **directly** to `UseNpgsql(dataSource, ...)` so that
connection/command types remain Npgsql-native. No wrapping data source is introduced.

`UseDsql` also accepts a plain `NpgsqlDataSource`, needed for test runs against the local
emulator and for user-managed authentication. The connector is a convenience, not a hard
dependency of the provider's behavior. The connector's own OCC retry helpers are **not** used;
we implement an EF `IExecutionStrategy` instead. Full rationale:
[`connector-amazon-auroradsql-npgsql.md`](connector-amazon-auroradsql-npgsql.md).

## 9. Relationship to upstream `efcore.pg`

`efcore.pg` already has a precedent for targeting a PG-compatible system with feature
limitations: `UseRedshift()`, which sets a flag on `NpgsqlOptionsExtension` /
`INpgsqlSingletonOptions` that the SQL generator and query translator consult.

The most maintainable long-term outcome is an upstream `UseDsql()` mode implemented the same
way (tracked in [npgsql/efcore.pg#3396](https://github.com/npgsql/efcore.pg/issues/3396)). A
standalone package cannot add fields to `NpgsqlOptionsExtension`, but it can register its own
`IDbContextOptionsExtension` and `Replace` the relevant services. We design for the standalone
package first, with the option to upstream later.

## 10. Open questions

- Whether DSQL exposes `pg_catalog`/information_schema sufficiently for `dotnet ef dbcontext
  scaffold` and `EnsureCreated`/`CanConnect`.
- Enforcement of the 3,000-row modification limit: detect and pre-split, or document and surface
  the server error?
- Which bulk-expansion patterns in `SaveChanges` could implicitly exceed the row limit.
- Behavior of `ExecuteUpdate`/`ExecuteDelete` and `SELECT ... FOR UPDATE` (DSQL allows locking
  only with equality predicates on a single table's primary key).
- Whether to ship an analyzer that flags unsupported mappings at compile time.

Known DSQL limitations surfaced by porting the `efcore.pg` suite (see
[`implementation-plan.md`](implementation-plan.md) Phase 6):

- **`xid` concurrency tokens are unsupported.** Npgsql maps `uint`/`[Timestamp]` row versions to
  the `xid` system column; DSQL has none. Use an application-managed concurrency token
  (`IsConcurrencyToken()` on a supported column) instead. The model validator rejects `xid` loudly.
- **`EnsureCreated`** works via `DsqlDatabaseCreator`: `Exists()` returns `true` (DSQL has a single
  `postgres` database) and `HasTables()` uses `information_schema` and ignores the `sys` schema,
  because DSQL does not expose `pg_catalog`. Migrations remain the recommended path.
- **One database only.** Test/tenant isolation must use schemas or separate clusters; code and
  fixtures that assume `CREATE DATABASE` per tenant do not work.

## 11. References

- Aurora DSQL compatibility: https://docs.aws.amazon.com/aurora-dsql/latest/userguide/working-with-postgresql-compatibility.html
- Aurora DSQL migration guide: https://docs.aws.amazon.com/aurora-dsql/latest/userguide/working-with-postgresql-compatibility-migration-guide.html
- Unsupported features: https://docs.aws.amazon.com/aurora-dsql/latest/userguide/working-with-postgresql-compatibility-unsupported-features.html
- `efcore.pg`: https://github.com/npgsql/efcore.pg
- DSQL upstream request: https://github.com/npgsql/efcore.pg/issues/3396
- Npgsql connector: https://github.com/awslabs/aurora-dsql-connectors/tree/main/dotnet/npgsql
- DSQL emulator: https://github.com/Dreamescaper/dsql-emulator

## 12. Related documents

- [`comparison-aurora-dsql-orms.md`](comparison-aurora-dsql-orms.md) — comparison with the AWS Labs adapter.
- [`connector-amazon-auroradsql-npgsql.md`](connector-amazon-auroradsql-npgsql.md) — connector decision.
- [`testing-with-dsql-emulator.md`](testing-with-dsql-emulator.md) — local integration testing.
- [`dsql-emulator-issues.md`](dsql-emulator-issues.md) — emulator issues and limitations.
- [`implementation-plan.md`](implementation-plan.md) — delivery plan.
