# Decision: use `Amazon.AuroraDsql.Npgsql`?

**Status:** accepted (open to revision)
**Question:** does it make sense to depend on the AWS Labs
[`Amazon.AuroraDsql.Npgsql`](https://github.com/awslabs/aurora-dsql-connectors/tree/main/dotnet/npgsql)
connector?

## Short answer

**Yes, but only as a data-source provider — never as a wrapper.** It solves DSQL IAM
authentication and connection invariants correctly, and it exposes the underlying
`NpgsqlDataSource`, so we can pass that straight to `UseNpgsql(...)` and keep Npgsql-native
connection/command types.

The provider must **also** accept a plain `NpgsqlDataSource`, because the connector always does
IAM token generation and endpoint/region resolution and therefore cannot be used against the
local emulator or a password-authenticated setup.

## What the package actually does

Verified from the connector source (`dotnet/npgsql/src/Amazon.AuroraDsql.Npgsql`):

- `DsqlDataSource.CreateAsync(DsqlConfig)` builds an `NpgsqlDataSourceBuilder` and:
  - installs an IAM token password provider (`UsePasswordProvider`) that generates a **fresh
    token per physical connection**;
  - enforces `SslMode=VerifyFull` + direct TLS negotiation, and `Enlist=false` (security
    invariants that cannot be overridden);
  - applies DSQL pool defaults: `MaxPoolSize=10`, `ConnectionLifetime=3300` (DSQL kills
    connections at 60 min), `ConnectionIdleLifetime=600`, `NoResetOnClose=true`;
  - resolves credentials via the AWS SDK default chain, supports profiles and custom
    credentials, detects the region from the endpoint, and accepts a bare 26-char cluster id.
- `DsqlDataSource.DataSource` exposes the underlying `NpgsqlDataSource`.
- `AuroraDsql.CreateDataSourceAsync(...)` is the static entry point; `DsqlDataSource` also offers
  `WithTransactionRetryAsync`, `ExecWithRetryAsync` and `OccRetry.IsOccError` helpers.
- It sets `application_name` (with an `OrmPrefix`, e.g. `"efcore"`).

## Options considered

| Option | Pros | Cons |
| --- | --- | --- |
| **A. Depend on the connector** | Correct IAM auth/refresh, region + cluster-id parsing, DSQL pool/SSL invariants, maintained by AWS Labs; exposes `NpgsqlDataSource` | Pulls in AWS SDK; IAM-only; does not work against the emulator; part of its retry API is redundant for EF |
| **B. Implement IAM auth ourselves** (Npgsql `UsePasswordProvider` + `Amazon.Runtime` signing) | Same dependency weight as A without the extra package; full control | Reimplements token generation, refresh, region resolution, credential chain — easy to get subtly wrong; more maintenance |
| **C. Require the caller to supply `NpgsqlDataSource`** | Lightest provider; no AWS SDK dependency; works with emulator, IAM, or any auth | Every user must wire up DSQL auth themselves; poor out-of-the-box experience |

## Decision

Adopt **A + C**:

1. Depend on `Amazon.AuroraDsql.Npgsql` in the main package for the convenience path.
2. `UseDsql` overloads:
   - `UseDsql(DsqlDataSource)` — calls `UseNpgsql(dsqlDataSource.DataSource, ...)`;
   - `UseDsql(NpgsqlDataSource)` — for tests, the emulator, or user-managed auth;
   - `UseDsql(string host, ...)` — builds a `DsqlDataSource` via the connector;
   - `UseDsql(IServiceProvider, ...)` — resolves a DI-registered data source.
3. **Never wrap** the connector's data source. Pass the underlying `NpgsqlDataSource` so
   `NpgsqlConnection`/`NpgsqlCommand` identity is preserved for all Npgsql features.
4. **Do not use the connector's retry helpers** for EF. Provide an EF `IExecutionStrategy`
   instead (the connector's `OccRetry.IsOccError` logic is a useful reference for detecting
   `SQLSTATE 40001`).
5. Set `OrmPrefix = "efcore"` on connector-created data sources when we build them.

## Consequences

- The provider carries an AWS SDK dependency for the convenience path. Option B remains open if
  the dependency proves too heavy; the internal design (accept `NpgsqlDataSource`) means users
  can always bypass the connector.
- **The connector cannot talk to the local emulator out of the box**, for two independent reasons:
  1. It requires a resolvable AWS region and credentials to mint an IAM token, even though the
     emulator ignores the token. A local host (`127.0.0.1`/`localhost`) does not match the
     `*.dsql[.x].<region>.on.aws` pattern `Util.ParseRegion` expects, so `AWS_REGION` (or
     `DsqlConfig.Region`) must be set; and `Token.ResolveCredentialsAsync` must find credentials
     (dummy `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` suffice — token generation is local SigV4
     presigning and makes no network calls).
  2. More importantly, it forces `SslMode = VerifyFull` and `SslNegotiation = Direct` *after* the
     `ConfigureConnectionString` callback, and these invariants cannot be overridden. The emulator
     serves a **self-signed certificate** by default, which `VerifyFull` rejects.
  It can be coerced to work (dummy credentials, a region, and a custom certificate with a matching
  SAN that the client trusts), but that is more work than building a plain `NpgsqlDataSource`.
  Integration tests therefore use a plain data source with `SslMode=Require` and a dummy password.
  See [`testing-with-dsql-emulator.md`](testing-with-dsql-emulator.md).

## References

- Connector: https://github.com/awslabs/aurora-dsql-connectors/tree/main/dotnet/npgsql
- Npgsql password providers: https://www.npgsql.org/doc/api/Npgsql.NpgsqlDataSourceBuilder.html
