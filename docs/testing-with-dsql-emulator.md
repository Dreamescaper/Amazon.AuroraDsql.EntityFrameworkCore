# Testing with the DSQL emulator

We test against a real Aurora DSQL cluster as little as possible. Local testing uses
[`Dreamescaper/dsql-emulator`](https://github.com/Dreamescaper/dsql-emulator): a PostgreSQL
wire-protocol proxy that enforces DSQL's dialect, transaction rules and optimistic concurrency.
It runs as a container and is driven with [Testcontainers for .NET](https://dotnet.testcontainers.org/).

Emulator bugs and limitations we hit are tracked in
[`dsql-emulator-issues.md`](dsql-emulator-issues.md).

## Test layers

| Layer | Backend | Runs | Purpose |
| --- | --- | --- | --- |
| Unit | none | every PR | Generated SQL snapshots, model conventions, validator messages, mapping selection |
| Integration | `dsql-emulator` container | every PR (Docker required) | Migrations, CRUD, transaction rules, OCC retry, `jsonb` collections, identifiers |
| Live | real DSQL cluster | manual / nightly | Verify against the service; catch emulator drift and IAM/auth behavior |

Unit tests matter most for this provider: because we override SQL generation rather than rewrite
SQL, almost all correctness can be asserted as strings without a database.

## Running the emulator

Image: `ghcr.io/dreamescaper/dsql-emulator:0.1.1` (pinned in the fixture for reproducibility).
Single container serves PostgreSQL (internal `5433`) and the proxy on `5432`. Readiness is
signalled by the log line `proxy listening`.

```bash
docker run --rm -p 5432:5432 ghcr.io/dreamescaper/dsql-emulator:0.1.1
```

## Testcontainers fixture

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;

public sealed class DsqlEmulatorFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("ghcr.io/dreamescaper/dsql-emulator:0.1.1") // pin a version
        .WithPortBinding(5432, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("proxy listening"))
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var host = _container.Hostname;
        var port = _container.GetMappedPublicPort(5432);

        ConnectionString =
            $"Host={host};Port={port};Username=admin;Password=an-iam-token;" +
            "Database=postgres;SSL Mode=Require;Pooling=false";
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

Create the data source directly with Npgsql — **do not** use `Amazon.AuroraDsql.Npgsql` here.
The connector forces `SslMode=VerifyFull` (not overridable), which rejects the emulator's
self-signed certificate, and it additionally requires AWS credentials plus a region to mint a
token the emulator never checks. Details:
[`connector-amazon-auroradsql-npgsql.md`](connector-amazon-auroradsql-npgsql.md).

```csharp
var dataSource = new NpgsqlDataSourceBuilder(fixture.ConnectionString).Build();
var options = new DbContextOptionsBuilder<MyContext>()
    .UseDsql(dataSource) // raw NpgsqlDataSource overload
    .Options;
```

`SSL Mode=Require` is enough: the emulator generates a self-signed certificate, and Npgsql does
not validate the chain in `Require` mode. The emulator supports `--no-tls` but the default image
terminates TLS, so keep TLS on to match DSQL.

## What the emulator lets us assert

- **Dialect rules** — ~45 rules over a parsed tree; unsupported features fail with real SQLSTATEs,
  so we can assert that the model validator / generator rejected them rather than relying on the
  server.
- **Transactions** — one DDL per transaction, DDL and DML in separate transactions, the
  3,000-row cap, aborted-transaction state.
- **Indexes** — `CREATE INDEX ASYNC` is accepted, answered with a `job_id`, and recorded in
  `sys.jobs`; synchronous `CREATE INDEX` is refused.
- **`ALTER TABLE`** — `NOT VALID` + `ALTER TABLE ASYNC ... VALIDATE CONSTRAINT` flows.
- **OCC** — conflicts surface as `SQLSTATE 40001` (`... (OC000)`), and the emulator supports
  deterministic conflict injection for retry-loop tests. *Confirm the exact injection mechanism
  and document it here when wiring Phase 5.*
- **Types** — the documented supported set, identity/sequence `CACHE` requirements, enums refused.

## Findings from building the fixture

Two emulator behaviours shaped the provider (both are correct DSQL semantics, not emulator bugs):

1. **Explicit isolation levels are rejected.** Npgsql maps `IsolationLevel.Unspecified` to an
   explicit `READ COMMITTED`, which DSQL refuses (`0A000: Unsupported isolation level`). The
   provider forces `RepeatableRead`, DSQL's fixed level.
2. **One DDL per transaction is enforced.** EF's `Migrator` opens a single transaction around the
   whole migration; the provider's `IMigrationCommandExecutor` commits and drops it, then lets each
   statement autocommit (`0A000: a transaction can include only one DDL statement` otherwise).

Also note: EF retrying execution strategies disallow `SaveChanges` inside a user-initiated
transaction, so integration tests wrap manual transactions in
`Database.CreateExecutionStrategy()` (or use `ExecuteInTransactionAsync`).

## Deterministic OCC tests

Do not rely on racing two transactions to produce a conflict: the emulator (like PostgreSQL)
blocks before failing, so timing differs from DSQL even though the outcome matches. Use the
emulator's deterministic conflict injection instead, and keep the live-cluster test for the
timing-sensitive case.

## CI

- Runs `dotnet test` for unit tests on every push.
- Runs integration tests in a Docker-enabled job; pin the emulator image tag.
- Live-cluster tests are excluded from PR CI and run manually/nightly with credentials.

## Running the same suites against a real cluster

Both suites are target-agnostic: they use the emulator by default and switch to a live cluster when
an endpoint is configured. The live path uses the `Amazon.AuroraDsql.Npgsql` connector, so IAM auth,
token refresh and `VerifyFull` TLS are handled for you.

```bash
export DSQL_CLUSTER_ENDPOINT=your-cluster.dsql.us-east-1.on.aws   # CLUSTER_ENDPOINT also works
export AWS_REGION=us-east-1            # optional; auto-detected from the endpoint
export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=...   # or a profile / role (SDK default chain)
```

Main integration suite (migrations, CRUD, jsonb, FKs, transactions):

```bash
dotnet test tests/Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests
```

efcore.pg compatibility harness (one class at a time — DSQL has a single database):

```bash
cd compat/efcorepg-functional
dotnet test --filter "FullyQualifiedName~.FindNpgsqlTest"
dotnet test --filter "FullyQualifiedName~.NorthwindWhereQueryNpgsqlTest"
```

Notes and caveats for live runs:

- **Migrations are applied to the cluster** and left in place (no `DROP DATABASE`). Re-runs are
  idempotent via `__EFMigrationsHistory`. Use a dedicated dev/test cluster.
- The harness **drops every table in non-system schemas** at store init, so do not point it at a
  cluster with data you care about.
- Provider support for catalog access: `DsqlDatabaseCreator.Exists()` returns `true` (DSQL has a
  single `postgres` database) and `HasTables()` uses `information_schema`, because DSQL does not
  expose `pg_catalog`. The harness's reset queries use `information_schema` for the same reason.
- AWS credentials and a reachable cluster are required; without the endpoint env vars the emulator
  path is used and nothing external is contacted.

CI runs live tests only via the manual **Live tests** workflow
(`.github/workflows/live.yml`); PR CI never touches a cluster.
