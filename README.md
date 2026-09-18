# Amazon.AuroraDsql.EntityFrameworkCore

An Entity Framework Core provider for **Amazon Aurora DSQL**, built on top of
[Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) and the
[Amazon.AuroraDsql.Npgsql](https://github.com/awslabs/aurora-dsql-connectors/tree/main/dotnet/npgsql) connector.

> **Status:** usable core — options/`UseDsql`, model conventions and validation, `jsonb`
> collections, migrations (`CREATE INDEX ASYNC`, FK `NOT VALID`, one DDL per transaction), no
> savepoints, and OCC retry. Verified against the `dsql-emulator`; not yet exercised against a
> live cluster. See [`docs/implementation-plan.md`](docs/implementation-plan.md) for what remains.

## Goal

Let developers use ordinary EF Core (`DbContext`, LINQ, migrations, `SaveChanges`) against
Aurora DSQL, which is PostgreSQL-wire-compatible but only supports a subset of PostgreSQL.

## Usage

```csharp
using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;

// Aurora DSQL via the AWS connector (IAM auth):
var dataSource = await AuroraDsql.CreateDataSourceAsync(new DsqlConfig
{
    Host = "your-cluster.dsql.us-east-1.on.aws",
});

services.AddDbContext<MyContext>(options => options.UseDsql(dataSource));
```

Or pass a plain `NpgsqlDataSource` (tests, the emulator, or user-managed auth):

```csharp
options.UseDsql(myNpgsqlDataSource);
```

Explicit transactions with OCC retry:

```csharp
await context.ExecuteInTransactionAsync(async ct =>
{
    context.Orders.Add(order);
    await context.SaveChangesAsync(ct);
});
```

Configuration: `options.UseDsql(dataSource, dsql => dsql.EnableIdentityColumns().SetMaxRetryCount(3))`.

`dsql.NullsFirst()` opts into `NULLS FIRST` so nulls sort first, as EF/SQL Server do by default
(PostgreSQL, and therefore DSQL, sorts nulls last); it is off by default.

Concurrency: DSQL has no `xid` column, so Npgsql `[Timestamp]`/`uint` row versions are rejected. Use
an application-managed token (`Property(w => w.Version).IsConcurrencyToken()`); DSQL's own conflicts
surface as `40001` and are retried. See [`docs/design.md`](docs/design.md) §7.

> **Package id note:** this project currently uses the same id (`Amazon.AuroraDsql.EntityFrameworkCore`)
> as the AWS Labs adapter. Both expose `UseDsql` and cannot be referenced together; the id must be
> resolved before any NuGet release. See [`docs/comparison-aurora-dsql-orms.md`](docs/comparison-aurora-dsql-orms.md).

## Approach

Reuse the Npgsql EF Core provider wholesale and override **only the components that produce
DSQL-incompatible SQL**, at SQL-generation time:

| DSQL constraint | Emitter in EF/Npgsql | Override |
| --- | --- | --- |
| `SAVEPOINT` around `SaveChanges` in a transaction | EF `BatchExecutor` | `IRelationalTransactionFactory` with `SupportsSavepoints => false` |
| `LOCK TABLE ... ACCESS EXCLUSIVE` on history table | `NpgsqlHistoryRepository` | custom `IHistoryRepository` |
| `SET TRANSACTION ISOLATION LEVEL` | `NpgsqlRelationalConnection` | subclass, force `RepeatableRead` |
| `CREATE INDEX` (not supported) | `NpgsqlMigrationsSqlGenerator` | emit `CREATE INDEX ASYNC` |
| FK added to existing table | `NpgsqlMigrationsSqlGenerator` | `NOT VALID` + `ALTER TABLE ASYNC ... VALIDATE CONSTRAINT` |
| 1 DDL per transaction | EF `IMigrationCommandExecutor` | per-command transaction executor |
| OCC conflict (`SQLSTATE 40001`) | EF `IExecutionStrategy` | DSQL retry strategy |
| Primitive collections (`int[]`, `List<string>`) | `NpgsqlTypeMappingSource` (native PG arrays) | map to a `jsonb` JSON array (like SQL Server), leveraging efcore.pg's existing JSON-collection translation |
| Unsupported types/features (enums, ranges, PostGIS, `CREATE TYPE`) | model + type mapping | model validator / type mapping source |

**No** ADO.NET connection/command wrappers, **no** regex on SQL text, **no** external
`dsql-lint` subprocess. See [`docs/design.md`](docs/design.md) for the rationale.

## Documentation

- [`docs/design.md`](docs/design.md) — architecture, constraints, design decisions, rejected alternatives.
- [`docs/comparison-aurora-dsql-orms.md`](docs/comparison-aurora-dsql-orms.md) — comparison with the AWS Labs adapter.
- [`docs/connector-amazon-auroradsql-npgsql.md`](docs/connector-amazon-auroradsql-npgsql.md) — whether to use the AWS Npgsql connector.
- [`docs/testing-with-dsql-emulator.md`](docs/testing-with-dsql-emulator.md) — local integration testing with Testcontainers.
- [`docs/dsql-emulator-issues.md`](docs/dsql-emulator-issues.md) — emulator bugs and limitations.
- [`docs/implementation-plan.md`](docs/implementation-plan.md) — phased delivery plan.

## Non-goals

- Forking `efcore.pg`.
- Supporting every PostgreSQL feature (Aurora DSQL does not).
- Reimplementing query translation; n/a for the large PostgreSQL-compatible subset.

## License

TBD.
