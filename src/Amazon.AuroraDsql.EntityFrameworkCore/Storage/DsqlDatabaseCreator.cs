using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Storage;

/// <summary>
/// Database creator adapted to Aurora DSQL:
/// <list type="bullet">
///   <item>the single <c>postgres</c> database always exists, so <c>Exists</c> never queries
///     <c>pg_database</c> (which DSQL does not expose);</item>
///   <item><c>HasTables</c> uses the SQL-standard <c>information_schema</c> (not <c>pg_class</c>,
///     also not exposed by DSQL) and ignores the <c>sys</c> schema.</item>
/// </list>
/// </summary>
internal sealed class DsqlDatabaseCreator : NpgsqlDatabaseCreator
{
    private const string HasTablesSql = """
SELECT CASE WHEN COUNT(*) = 0 THEN FALSE ELSE TRUE END
FROM information_schema.tables
WHERE table_type = 'BASE TABLE'
  AND table_schema NOT IN ('pg_catalog', 'information_schema', 'sys')
""";

    private readonly INpgsqlRelationalConnection _connection;
    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;

    public DsqlDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        INpgsqlRelationalConnection connection,
        IRawSqlCommandBuilder rawSqlCommandBuilder)
        : base(dependencies, connection, rawSqlCommandBuilder)
    {
        _connection = connection;
        _rawSqlCommandBuilder = rawSqlCommandBuilder;
    }

    public override bool Exists() => true;

    public override Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public override bool HasTables()
        => Dependencies.ExecutionStrategy.Execute(
            _connection,
            (_, connection) => (bool)_rawSqlCommandBuilder
                .Build(HasTablesSql)
                .ExecuteScalar(
                    new RelationalCommandParameterObject(
                        connection,
                        null,
                        null,
                        Dependencies.CurrentContext.Context,
                        Dependencies.CommandLogger))!,
            verifySucceeded: null);

    public override Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
        => Dependencies.ExecutionStrategy.ExecuteAsync(
            _connection,
            async (_, connection, ct) => (bool)(await _rawSqlCommandBuilder
                .Build(HasTablesSql)
                .ExecuteScalarAsync(
                    new RelationalCommandParameterObject(
                        connection,
                        null,
                        null,
                        Dependencies.CurrentContext.Context,
                        Dependencies.CommandLogger),
                    cancellationToken: ct)
                .ConfigureAwait(false))!,
            verifySucceeded: null,
            cancellationToken);
}
