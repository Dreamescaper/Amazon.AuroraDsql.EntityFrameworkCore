# efcore.pg functional-suite compatibility harness

**Exploratory / not part of the shipped provider.** Lives on the
`efcorepg-functional-suite` branch.

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

```bash
# start the emulator
docker run -d --name dsql-emu -p 55432:5432 ghcr.io/dreamescaper/dsql-emulator:0.1.1

export DSQL_TEST_CONNECTION="Host=127.0.0.1;Port=55432;Username=admin;Password=token;Database=postgres;SSL Mode=Require;Pooling=false"

dotnet test --filter "FullyQualifiedName~FindNpgsqlTest"
```

## Ported so far

- `FindNpgsqlTest`

More classes are copied in as the failures are triaged. Each fixture's store is shared and reset
per run; classes that need multi-database isolation or unsupported features are expected to fail.
