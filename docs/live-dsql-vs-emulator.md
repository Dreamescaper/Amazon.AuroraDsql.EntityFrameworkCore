# Live Aurora DSQL vs. the emulator

The suites run against the emulator by default and against a real cluster when one is configured.
This page records the behaviour differences found by running both, and is the place to look before
asserting emulator behaviour as equivalent.

For what the emulator itself gets wrong (and issues filed upstream), see
[`dsql-emulator-issues.md`](dsql-emulator-issues.md).

> **Emulator 0.3.0 (2026-09-18).** The two "more permissive than DSQL" DDL divergences are closed:
> `integer` identity columns and `ALTER TABLE ... ADD CONSTRAINT ... PRIMARY KEY`/`UNIQUE` are now
> refused like DSQL. The remaining filed divergence is the `LEFT JOIN LATERAL` planning bug
> (emulator issue #4).

## Running against a real cluster

**Preferred: AWS credentials (tokens are refreshed per connection).** The connector mints and
refreshes IAM tokens, which matters because a full suite run outlasts a single 15-minute token.

```bash
export DSQL_CLUSTER_ENDPOINT=your-cluster.dsql.eu-central-1.on.aws
export AWS_REGION=eu-central-1
export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=... AWS_SESSION_TOKEN=...   # or profile/role

dotnet test tests/Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests
cd compat/efcorepg-functional && dotnet test --filter "FullyQualifiedName~.FindNpgsqlTest"
```

**Short runs with a single token.** DSQL also accepts a presigned IAM token as the password. A
`DSQL_TEST_CONNECTION` value wins over the endpoint, so both suites take it:

```bash
export DSQL_TEST_CONNECTION="Host=your-cluster.dsql.eu-central-1.on.aws;Port=5432;Username=admin;Password=<token>;Database=postgres;SSL Mode=VerifyFull;No Reset On Close=true;Pooling=false"
```

A token can be generated with a longer lifetime (CLI/SDK default is 15 minutes, the console default
is 1 hour, and the maximum is 604,800 seconds — one week):

```bash
aws dsql generate-db-connect-admin-auth-token \
  --hostname your-cluster.dsql.eu-central-1.on.aws --region eu-central-1 --expires-in 86400
```

Caveats: DSQL also rejects the connection if the *IAM role session* behind the token has expired, so
a long token only helps while that session is valid (observed both ~1 hour and ~15 minutes on the
same cluster); and the token is not refreshed, it is simply reused. Never commit a token. For an
unattended full run, prefer credentials so the connector refreshes tokens per connection.

A presigned-token run that outlives the role session **does not fail fast**: every subsequent
command gets `08006 unable to accept connection, access denied` (`Hint: The security token ... has
expired`), Npgsql classifies that as transient, and `DsqlExecutionStrategy` retries it six times
with exponential backoff. A full `NorthwindWhere` run therefore spends ~1 minute per remaining test
and looks hung (observed 2026-09-18: the two `Where_contains_on_navigation` tests take ~10 minutes
on their own, the role session then expired, and 45 tests failed with `RetryLimitExceededException`
wrapping `08006`). For a multi-suite live run use AWS credentials, or one short window per suite and
skip the two known-slow tests.

`Where_contains_on_navigation` is the slow query: a correlated `EXISTS`/`IN` over `Customers` ×
`Orders` (SQL in the live logs). The curated live workflow (`.github/workflows/live.yml`) excludes it
with `--filter "FullyQualifiedName~.<suite>&FullyQualifiedName!~Where_contains_on_navigation"` so the
session window isn't spent on a known-slow, known-failing query; run it deliberately on its own. The
emulator runs it fine, so the exclusion only bites live runs.

## Results

A first full live pass (short-lived token) covered the provider suite and the non-Northwind
harness suites. A second pass on 2026-09-18 ran the Northwind suites (slow
`Where_contains_on_navigation` excluded); the fast ones completed before the token's STS session
expired:

| Suite | Live cluster |
| --- | ---: |
| `FindNpgsqlTest` | **411/411** |
| `NorthwindAsTrackingQueryNpgsqlTest` | **6/6** |
| `NorthwindQueryTaggingQueryNpgsqlTest` | **9/9** |
| `NorthwindSqlQueryNpgsqlTest` | **9/9** |
| `NorthwindChangeTrackingQueryNpgsqlTest` | **17/17** |
| `NorthwindAsNoTrackingQueryNpgsqlTest` | **24/24** |
| `NorthwindCompiledQueryNpgsqlTest` | **32/32** |
| `NorthwindNavigationsQueryNpgsqlTest` | **146/146** |
| `NorthwindSetOperationsQueryNpgsqlTest` | **192/192** |
| `NorthwindIncludeNoTrackingQueryNpgsqlTest` | **236/236** |
| `NorthwindSplitIncludeNoTrackingQueryNpgsqlTest` | **236/236** |
| `NorthwindEFPropertyIncludeQueryNpgsqlTest` | **238/238** |

That is 1,556 tests with 0 failures. `NorthwindAggregateOperators`, `NorthwindGroupBy`,
`NorthwindWhere` and `NorthwindMiscellaneous` were attempted next, but the STS session behind that
token expired (~15 minutes this time), after which every command returned `08006` and
`DsqlExecutionStrategy` retried it 6×; all 34 observed failures were that, with no genuine
assertion/SQL diff. Those four still need a credentials-based run (or one short window each).

| Suite | Emulator | Live cluster |
| --- | --- | --- |
| Provider integration (`tests/.../IntegrationTests`) | 9/9 | **9/9** |
| `FindNpgsqlTest` | 411/411 | **411/411** |
| `CompositeKeysQueryNpgsqlTest` | 14/14 | **14/14** |
| `CompositeKeysSplitQueryNpgsqlTest` | 14/14 | **14/14**¹ |
| `FunkyDataQueryNpgsqlTest` | 42/42 | **42/42** |
| `CharacterQueryNpgsqlTest` | 4/4 | **4/4** |
| `NavigationTest` | 2/2 | **2/2** |
| `AdHocMiscellaneousQueryNpgsqlTest` | 69/71 (2 skipped) | **65/71** (4 failed, 2 skipped)² |
| `AdHocNavigationsQueryNpgsqlTest` | 24/25 | **24/25** (1 failed)³ |
| `NorthwindWhereQueryNpgsqlTest` | 417/421 | 415/421 |
| `NorthwindMiscellaneousQueryNpgsqlTest` | 962/963 (0 failed) | 954/963 (8 failed) |
| `NorthwindSetOperationsQueryNpgsqlTest` | 192/192 | 192/192 |
| `NorthwindAggregateOperatorsQueryNpgsqlTest` | 422/422 | 422/422 |

¹ Failed once with a transient `40001`, passed on retry — see below.
² `0A000: ddl and dml are not supported in the same transaction` and `40001` conflicts — see below.
³ The model validator rejects a `bytea` index (`Comment.BlogName`), same as on the emulator.

The provider integration suite passes unchanged on a real cluster: migrations (`CREATE TABLE`,
`CREATE INDEX ASYNC`, `ALTER TABLE ... ADD CONSTRAINT ... NOT VALID` + `ALTER TABLE ASYNC ...
VALIDATE CONSTRAINT`), `jsonb` collections, foreign keys, explicit transactions without savepoints
and `ExecuteInTransactionAsync`. `FindNpgsqlTest` also passes, which exercises `EnsureCreated` of a
large model with `int` identity keys widened to `bigint`.

## Differences found

### Identity columns must be `bigint`

Real DSQL rejects an integer identity column:

```
0A000: datatype integer not supported, identity column type must be bigint
```

The emulator accepts `integer GENERATED ... AS IDENTITY` (filed upstream as
[dsql-emulator#2](https://github.com/Dreamescaper/dsql-emulator/issues/2)).

**Provider change:** `int` primary keys that use identity are widened to `bigint` at the model level
(a `system` value converter to `long`), so `int` keys keep working on DSQL. Identity columns of any
other non-`bigint` type are rejected by the model validator with an actionable message.

**Harness changes:** the adapted `Northwind.sql` uses `BIGINT GENERATED BY DEFAULT AS IDENTITY
(CACHE 1)` for `SERIAL`, and the Npgsql-specific `HasColumnType("int")` overrides for `EmployeeID` /
`ReportsTo` were removed so the widening applies.

### `information_schema.routines` exposes a `sys` routine

On a real cluster, `information_schema.routines` lists `sys.supported_datatypes`; a naive
"drop everything not in `pg_catalog`/`information_schema`" loop fails with
`42501: must be owner of routine sys.supported_datatypes`. The harness now excludes `sys` (and the
provider's `HasTables` already did).

### Schema changes conflict with concurrent DML (`OC001`)

Loading the Northwind script live failed on an `INSERT` shortly after the DDL with
`40001: schema has been updated by another transaction (OC001)`. DSQL ties a statement to a schema
version, so a DML statement that runs while a schema change is in flight is rejected and must be
retried. The single-node emulator does not reproduce this. The harness now retries `40001` per
statement.

### `ALTER TABLE ... ADD CONSTRAINT ... PRIMARY KEY` is unsupported

Live, `0A000: unsupported ALTER TABLE ADD CONSTRAINT statement`. DSQL supports inline primary keys
and `ADD CONSTRAINT ... NOT VALID` for CHECK/FOREIGN KEY, but not adding a PRIMARY KEY/UNIQUE to an
existing table. The emulator accepts it (filed upstream as
[dsql-emulator#3](https://github.com/Dreamescaper/dsql-emulator/issues/3)). The adapted Northwind
script folds those primary keys into their `CREATE TABLE`.

### Northwind harness adaptations

To make the full Northwind dataset load on DSQL (`tools/adapt_northwind.py`):

- `SERIAL` → `BIGINT GENERATED BY DEFAULT AS IDENTITY (CACHE 1)` (DSQL requires `bigint` identity;
  the provider widens the model's `int` keys to match).
- Secondary indexes are omitted: `CREATE INDEX ASYNC` builds in the background, and an in-progress
  build conflicts with the data load (`OC001`).
- `ALTER TABLE ... ADD CONSTRAINT ... PRIMARY KEY` is folded into `CREATE TABLE`.
- Foreign keys are moved out of `CREATE TABLE` and added after the data load as `NOT VALID` (no
  async validation job). A `NOT VALID` FK is still enforced for new rows, so they must follow the
  inserts.
- Single-row `INSERT`s are batched into multi-row `INSERT ... VALUES` — the **full** dataset is
  kept, but the script shrinks from ~3,400 statements to ~56.
- Every statement is retried on `40001`.

With that, the harness's emulator results are unchanged (`NorthwindWhere` 417/421,
`NorthwindMiscellaneous` 962/963); a full live Northwind run is pending credentials that outlast
the script load (see below).

### DDL and DML in the same transaction (`0A000`)

Live, `AdHocMiscellaneousQueryNpgsqlTest` reports
`0A000: ddl and dml are not supported in the same transaction`. This is **not** an emulator
divergence: `0.3.0` (and `0.2.1`) reject the same thing —
`BEGIN; CREATE TABLE ...; INSERT ...; COMMIT;` returns `0A000` on the emulator too. So the live
failure is about statement sequencing on the harness's `EnsureCreated` + seed path, which must be
putting DDL and DML in one transaction in a way the emulator's check does not catch (e.g. a
pipelined batch), or the live run hit a different pair of statements. Open: capture the exact
statement pair on a real cluster and make the provider/harness separate them.

### Transient `40001` during suite setup

`CompositeKeysSplitQueryNpgsqlTest` failed all 14 tests once with `40001` at store
initialisation (drop/create) and passed on a re-run. Worth understanding whether the store reset
pattern (many `DROP TABLE` statements) can collide on a real cluster, and whether the OCC execution
strategy should cover migration/setup commands.

### DSQL has no composite/row types (`42804`)

`NorthwindMiscellaneousQueryNpgsqlTest.Complex_nested_query...` (2 tests) fails with
`42804: attribute 1 of type "Orders" has wrong type`. DSQL does not support `CREATE TYPE`/composite
types; the emulator's PostgreSQL does. Filed upstream as
[dsql-emulator#4](https://github.com/Dreamescaper/dsql-emulator/issues/4).

### `Skip`/`Take` collection projections pick different rows (6)

`Projection_skip_collection_projection`, `Projection_take_collection_projection` and
`Projection_skip_take_collection_projection` fail live with `Assert.Equal` count mismatches (e.g.
expected 31, actual 39) while passing on the emulator. Likely a difference in `ORDER BY`/`LIMIT` row
selection when the ordering is not fully deterministic; open — confirm whether it is ordering, data,
or a translation difference.

### `Where_contains_on_navigation` exhausts OCC retries (2)

Each variant took ~4m44s and failed with `RetryLimitExceededException` (6 `DsqlExecutionStrategy`
attempts) under `TimeoutException: Timeout during reading attempt`. The `Contains`-on-navigation
translation appears to produce a slow query on DSQL; open — capture the SQL and a plan.

## Known caveats from running live

- **`List<object>` / `object[]` `Contains` over a widened int key.** After the `int` → `bigint`
  widening, `Where_list_object_contains_over_value_type` and
  `Where_array_of_object_contains_over_value_type` fail with
  `Expression of type 'System.Object' cannot be used for parameter of type 'System.Int32'`. This is
  an EF translation interaction with the value converter on the key, not a DSQL behaviour
  difference; 4 tests, tracked in the plan. Reproduced on the emulator. Workaround: use a typed
  collection (`int[]`/`List<int>`), which translates to a `jsonb`/array `Contains` and passes.
- **Destructive.** The harness drops every table in non-system schemas, and the integration suite
  applies migrations. Use a throwaway cluster.
- **Single database.** Isolation between fixtures (and tenants) needs schemas or separate clusters;
  run one harness class per invocation.
- **Set-up flakiness under OCC.** Live runs can surface `40001` during fixture/reset operations;
  re-run a class before treating a failure as real.
