# Amazon.AuroraDsql.EntityFrameworkCore

An Entity Framework Core provider for **Amazon Aurora DSQL**, built on top of
[Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) and the
[Amazon.AuroraDsql.Npgsql](https://github.com/awslabs/aurora-dsql-connectors/tree/main/dotnet/npgsql) connector.

> **Status:** design phase. No working implementation yet — see
> [`docs/implementation-plan.md`](docs/implementation-plan.md).

## Goal

Let developers use ordinary EF Core (`DbContext`, LINQ, migrations, `SaveChanges`) against
Aurora DSQL, which is PostgreSQL-wire-compatible but only supports a subset of PostgreSQL.

## Approach

Reuse the Npgsql EF Core provider wholesale and override **only the components that produce
DSQL-incompatible SQL**, at SQL-generation time:

| DSQL constraint | Emitter in EF/Npgsql | Override |
| --- | --- | --- |
| `SAVEPOINT` around `SaveChanges` in a transaction | EF `BatchExecutor` | `IRelationalTransactionFactory` with `SupportsSavepoints => false` |
| `LOCK TABLE ... ACCESS EXCLUSIVE` on history table | `NpgsqlHistoryRepository` | custom `IHistoryRepository` |
| `SET TRANSACTION ISOLATION LEVEL` | `NpgsqlRelationalConnection` | subclass, force `Unspecified` |
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
- [`docs/implementation-plan.md`](docs/implementation-plan.md) — phased delivery plan.

## Non-goals

- Forking `efcore.pg`.
- Supporting every PostgreSQL feature (Aurora DSQL does not).
- Reimplementing query translation; n/a for the large PostgreSQL-compatible subset.

## License

TBD.
