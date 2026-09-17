# efcore.pg functional-suite compatibility harness

**Exploratory / not part of the shipped provider.** Deliberately not in the solution, so it is a
separate project from the main tests; it is built by its own CI job.

Goal: copy test classes from
[`npgsql/efcore.pg`](https://github.com/npgsql/efcore.pg) `v10.0.3` (`test/EFCore.PG.FunctionalTests`)
largely unchanged and run them against this provider backed by the `dsql-emulator`, to see what
passes and what fails. Bring-up findings are in [`FINDINGS.md`](FINDINGS.md).

## How it works

efcore.pg's tests reference `NpgsqlTestStore`, `NpgsqlTestStoreFactory`, `NpgsqlTestHelpers` and
`TestEnvironment` from `Microsoft.EntityFrameworkCore.TestUtilities`, and the shared base classes
from `Microsoft.EntityFrameworkCore.Relational.Specification.Tests`. This harness ships **its own
copies** of those `TestUtilities` types under the same names, adapted to:

- use the single DSQL database (no `CREATE DATABASE`; reset by dropping all tables),
- register the provider via `UseDsql` + `AddEntityFrameworkDsql`,
- read the connection string from `DSQL_TEST_CONNECTION`.

Because the type names and namespaces match, copied test files compile without edits.

## Running

> Pinned to emulator `0.2.1` (fixes the `v0.2.0` Npgsql protocol regression) — see
> [`../../docs/dsql-emulator-issues.md`](../../docs/dsql-emulator-issues.md). The harness also forces
> `TZ=UTC` at startup because DSQL runs in UTC and some spec tests mix client `DateTime.Today` with
> the server's `now()`.

Emulator (default):

```bash
docker run -d --name dsql-emu -p 55432:5432 ghcr.io/dreamescaper/dsql-emulator:0.2.1

export DSQL_TEST_CONNECTION="Host=127.0.0.1;Port=55432;Username=admin;Password=token;Database=postgres;SSL Mode=Require;Pooling=false"

dotnet test --filter "FullyQualifiedName~FindNpgsqlTest"
```

Real Aurora DSQL cluster (IAM auth via the `Amazon.AuroraDsql.Npgsql` connector):

```bash
export DSQL_CLUSTER_ENDPOINT=your-cluster.dsql.us-east-1.on.aws   # CLUSTER_ENDPOINT also works
export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=...           # or a profile / role

dotnet test --filter "FullyQualifiedName~FindNpgsqlTest"
```

The store drops every table in non-system schemas at init, so use a throwaway cluster. Run one
test class per invocation (`DSQL_CLUSTER_ENDPOINT` / `DSQL_TEST_CONNECTION` choose the target).

## Ported so far

`FindNpgsqlTest`, `ManyToManyLoadNpgsqlTest`, `FieldMappingNpgsqlTest`, `AdHocMiscellaneousQueryNpgsqlTest`,
`AdHocNavigationsQueryNpgsqlTest`, `EntitySplittingQueryNpgsqlTest`, `FunkyDataQueryNpgsqlTest`,
`CompositeKeysQueryNpgsqlTest`, `CompositeKeysSplitQueryNpgsqlTest`, `CharacterQueryNpgsqlTest`,
`NavigationTest`, `ConnectionSpecificationTest`, `BuiltInDataTypesNpgsqlTest`, `BatchingTest`,
`OptimisticConcurrencyNpgsqlTest`, `DataBindingNpgsqlTest`, `CustomConvertersNpgsqlTest`,
`ConvertToProviderTypesNpgsqlTest`, `NpgsqlValueGenerationScenariosTest`, `DefaultValuesTest`, and
the Northwind query suites (Where, Miscellaneous, GroupBy, Navigations, AggregateOperators,
CompiledQuery, Include/SplitInclude, SetOperations, tracking, SqlQuery, QueryTagging) — the last
group runs against a DSQL-adapted `Northwind.sql` produced by [`tools/adapt_northwind.py`](tools/adapt_northwind.py).

Results and classification: [`FINDINGS.md`](FINDINGS.md). Run one class at a time — DSQL has a
single database, so fixtures clobber each other if run together.
