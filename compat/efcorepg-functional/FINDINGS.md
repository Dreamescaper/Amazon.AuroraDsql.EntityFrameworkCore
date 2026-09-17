# efcore.pg functional-suite bring-up: findings

Running a subset of `npgsql/efcore.pg` `v10.0.3` functional tests against this provider on the
`dsql-emulator:0.1.1`. Each suite is run **one class at a time** (see "Harness limitations").

## Results

| Test class | Passed | Failed | Skipped | Dominant cause of failure |
| --- | ---: | ---: | ---: | --- |
| `FindNpgsqlTest` | 411 | 0 | 0 | — |
| `ManyToManyLoadNpgsqlTest` | 358 | 0 | 0 | — |
| `FieldMappingNpgsqlTest` | 156 | 11 | 0 | Harness (transaction/connection mismatch) |
| `ConnectionSpecificationTest` | 2 | 11 | 0 | Harness (`Northwind.sql` missing) |
| `BuiltInDataTypesNpgsqlTest` | 0 | 33 | 6 | `hstore` extension (unsupported by DSQL) |
| `BatchingTest` | 0 | 12 | 0 | `xid` concurrency token (unsupported) |
| `OptimisticConcurrencyNpgsqlTest` | 0 | 48 | 1 | `xid` concurrency token (unsupported) |
| `DataBindingNpgsqlTest` | 0 | 58 | 0 | `xid` concurrency token (unsupported) |
| `CustomConvertersNpgsqlTest` | 0 | 56 | 4 | Index on `bytea` (not indexable in DSQL) |
| `ConvertToProviderTypesNpgsqlTest` | 0 | 29 | 3 | Index on `bytea` (not indexable in DSQL) |
| `NpgsqlValueGenerationScenariosTest` | 0 | 13 | 0 | Uses the store's raw `NpgsqlConnection` → `READ COMMITTED`; some schema interference |
| `DefaultValuesTest` | 0 | 1 | 0 | Transient error (needs triage) |

A whole-project run (all fixtures in one process) fails almost everything with
`42P01: relation "..." does not exist` — that is cross-fixture interference, not a provider bug
(see below).

## Provider bugs found and fixed

1. **Service lifetimes.** `IModelValidator` and `IRelationalTypeMappingSource` were registered
   `Scoped`, but EF Core registers them `Singleton`. Scope validation (`validateScopes: true`, as
   the spec fixtures use) fails with
   `Cannot consume scoped service 'IRelationalTypeMappingSource' from singleton ...`.
2. **Identity columns need an explicit cache.** DSQL rejects `GENERATED ... AS IDENTITY` without a
   cache (`0A000: identity column is not supported without an explicit cache size`). The generator
   now always emits one: `CACHE 1` by default, `CACHE n` when `EnableIdentityColumns` is set.
   (Npgsql omits `CACHE` when it is 1, so `IdentityDefinition` is overridden to force it.)
3. **Type aliases rejected.** The model validator compared `StoreTypeNameBase` against a canonical
   set, so `HasColumnType("int")`, `"int8"`, `"varchar"`, `"timestamptz"`, `"bool"`, `"decimal"`, etc.
   were wrongly rejected. Aliases are now normalized before the check.

## DSQL limitations surfaced (expected; model validation rejects them loudly)

- **`xid` concurrency tokens** (`uint`/`[Timestamp]` row versions, e.g. `Blog.Version`,
  `Chassis.Version`). DSQL has no `xid` system column. Use an application-managed concurrency
  token (`Property(x => x.Version).IsConcurrencyToken()`) on a supported type instead.
- **`hstore` and other extensions.** DSQL has no `CREATE EXTENSION`.
- **Indexes on `bytea`** (and `json`, `jsonb`, `timetz`, `interval`). DSQL cannot index them.

These are the intended "fail loudly at model time" behavior; the tests that rely on them cannot
pass on DSQL by design.

## Harness limitations (not provider bugs)

- **Single database.** DSQL (and the emulator) has one database, but the spec suites assume a
  separate database per fixture (`CREATE DATABASE`/`DROP DATABASE`). Running multiple fixtures in
  one process clobbers each other. Run one class at a time; fixtures drop all tables on init.
- **`EnsureCreated` is unreliable.** Npgsql's `HasTables()` counts any non-system schema, and DSQL
  exposes the `sys` schema, so `EnsureCreated` skips table creation. The harness calls
  `IRelationalDatabaseCreator.CreateTablesAsync()` directly. This is a real provider consideration
  for `EnsureCreated` users — tracked on `main`.
- **`Northwind.sql`** is not shipped, so `ConnectionSpecificationTest` fails to find it.
- **Transaction/connection mismatch.** Some tests use the store's `NpgsqlConnection` with a
  context built on the shared data source, so the transaction is not associated with the
  connection (`FieldMappingNpgsqlTest`, 11 failures).
- **Raw-connection transactions.** Tests that call `store.Connection.BeginTransaction()` bypass
  the provider's connection and get Npgsql's default `READ COMMITTED`, which DSQL rejects
  (`NpgsqlValueGenerationScenariosTest`, 9 failures). This is a harness/DSQL-semantics artifact,
  not a provider path (a real app would use `context.Database.BeginTransaction`).

## Reproducing

```bash
docker run -d --name dsql-emu -p 55432:5432 ghcr.io/dreamescaper/dsql-emulator:0.1.1
export DSQL_TEST_CONNECTION="Host=127.0.0.1;Port=55432;Username=admin;Password=token;Database=postgres;SSL Mode=Require;Pooling=false"
dotnet test --filter "FullyQualifiedName~.FindNpgsqlTest"       # 411 pass
dotnet test --filter "FullyQualifiedName~.ManyToManyLoadNpgsqlTest"  # 358 pass
```
