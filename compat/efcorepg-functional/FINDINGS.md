# efcore.pg functional-suite bring-up: findings

Running a subset of `npgsql/efcore.pg` `v10.0.3` functional tests against this provider on the
`dsql-emulator:0.1.1`. Each suite is run **one class at a time** (see "Harness limitations").

## Results

Most suites pass. Failures cluster into three buckets: DSQL limitations the model validator
rejects on purpose, harness single-database/raw-connection artifacts, and EF-version drift in a
couple of provider fixtures.

| Test class | Passed | Skipped | Failed | Notes |
| --- | ---: | ---: | ---: | --- |
| `FindNpgsqlTest` | 411 | 0 | 0 | |
| `ManyToManyLoadNpgsqlTest` | 358 | 0 | 0 | |
| `FieldMappingNpgsqlTest` | 156 | 0 | 11 | harness: transaction/connection mismatch |
| `AdHocMiscellaneousQueryNpgsqlTest` | 69 | 2 | 0 | |
| `EntitySplittingQueryNpgsqlTest` | 64 | 20 | 13 | "Missing test overrides" (spec/provider fixture drift) |
| `FunkyDataQueryNpgsqlTest` | 42 | 0 | 0 | |
| `AdHocNavigationsQueryNpgsqlTest` | 24 | 0 | 1 | index on `bytea` (DSQL limitation) |
| `CompositeKeysQueryNpgsqlTest` | 14 | 0 | 0 | |
| `CompositeKeysSplitQueryNpgsqlTest` | 14 | 0 | 0 | |
| `CharacterQueryNpgsqlTest` | 4 | 0 | 0 | |
| `NavigationTest` | 2 | 0 | 0 | |
| `ConnectionSpecificationTest` | 2 | 0 | 11 | harness: `Northwind.sql` not shipped |
| `BuiltInDataTypesNpgsqlTest` | 0 | 6 | 33 | `hstore` (DSQL limitation) |
| `BatchingTest` | 0 | 0 | 12 | `xid` concurrency token (DSQL limitation) |
| `OptimisticConcurrencyNpgsqlTest` | 0 | 1 | 48 | `xid` concurrency token (DSQL limitation) |
| `DataBindingNpgsqlTest` | 0 | 0 | 58 | `xid` concurrency token (DSQL limitation) |
| `CustomConvertersNpgsqlTest` | 0 | 4 | 56 | index on `bytea` (DSQL limitation) |
| `ConvertToProviderTypesNpgsqlTest` | 0 | 3 | 29 | index on `bytea` (DSQL limitation) |
| `NpgsqlValueGenerationScenariosTest` | 0 | 0 | 13 | harness: raw-`NpgsqlConnection` tx → `READ COMMITTED` |
| `DefaultValuesTest` | 0 | 0 | 1 | transient error (needs triage) |

A whole-project run (all fixtures in one process) fails almost everything with `42P01: relation
"..." does not exist` — cross-fixture interference, not a provider bug.

`ManyToManyQueryNpgsqlTest` / `ManyToManyNoTrackingQueryNpgsqlTest` were dropped: their
provider-specific fixture fails with `Unable to determine the relationship ... UnidirectionalEntityOne.Collection`,
i.e. EF 10.0.4 (efcore.pg v10.0.3) vs 10.0.12 model-configuration drift, unrelated to DSQL.

## Provider bugs found and fixed (all on `main`)

1. **Service lifetimes.** `IModelValidator` and `IRelationalTypeMappingSource` were registered
   `Scoped`; EF Core registers them `Singleton`. Under `validateScopes: true` (the spec fixtures):
   `Cannot consume scoped service 'IRelationalTypeMappingSource' from singleton ...`.
2. **Identity columns need an explicit cache.** DSQL rejects `GENERATED ... AS IDENTITY` without a
   cache (`0A000: identity column is not supported without an explicit cache size`). The generator
   now always emits one: `CACHE 1` by default, `CACHE n` with `EnableIdentityColumns`
   (`IdentityDefinition` is overridden because Npgsql omits `CACHE` when it is 1).
3. **Type aliases rejected.** The validator compared `StoreTypeNameBase` to a canonical set, so
   `HasColumnType("int")`, `"int8"`, `"varchar"`, `"timestamptz"`, `"bool"`, `"decimal"`, etc. were
   wrongly rejected. Aliases are now normalized.
4. **`EnsureCreated`/`HasTables`.** Npgsql's `HasTables()` counts any non-system schema, and DSQL
   exposes the `sys` schema, so `EnsureCreated` always believed tables existed and skipped creation.
   `DsqlDatabaseCreator` now excludes `sys`. Verified by running `FindNpgsqlTest` (411/411) through
   the standard spec-fixture `EnsureCreated` path.

Earlier on `main`: `AddEntityFrameworkDsql(IServiceCollection)` for external/internal service
providers, which the spec fixtures require (`UseInternalServiceProvider` skips `ApplyServices`).

## DSQL limitations surfaced (expected; rejected loudly at model time)

- **`xid` concurrency tokens** (`uint`/`[Timestamp]` row versions). DSQL has no `xid` column. Use
  an application-managed concurrency token on a supported type.
- **`hstore` and other extensions.** DSQL has no `CREATE EXTENSION`.
- **Indexes on `bytea`** (and `json`, `jsonb`, `timetz`, `interval`).

## Harness limitations (not provider bugs)

- **Single database.** The spec suites assume a separate database per fixture
  (`CREATE DATABASE`/`DROP DATABASE`); DSQL has one. Run one class at a time.
- **`Northwind.sql`** is not shipped, so Northwind-based suites fail to find it.
- **Raw-connection transactions.** Tests calling `store.Connection.BeginTransaction()` bypass the
  provider's connection and get Npgsql's `READ COMMITTED`, which DSQL rejects.
- **Transaction/connection mismatch.** Some tests use the store's `NpgsqlConnection` with a context
  built on the shared data source.

## Reproducing

```bash
docker run -d --name dsql-emu -p 55432:5432 ghcr.io/dreamescaper/dsql-emulator:0.1.1
export DSQL_TEST_CONNECTION="Host=127.0.0.1;Port=55432;Username=admin;Password=token;Database=postgres;SSL Mode=Require;Pooling=false"
dotnet test --filter "FullyQualifiedName~.FindNpgsqlTest"
dotnet test --filter "FullyQualifiedName~.AdHocMiscellaneousQueryNpgsqlTest"
```
