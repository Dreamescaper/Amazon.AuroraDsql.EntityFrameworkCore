using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Storage;

/// <summary>
/// Database creator that ignores Aurora DSQL's <c>sys</c> schema when checking whether any tables
/// exist. Npgsql's check counts every non-system schema, so with the <c>sys</c> schema present it
/// always reports that tables exist and <c>EnsureCreated</c> skips creating the model's tables.
/// </summary>
internal sealed class DsqlDatabaseCreator : NpgsqlDatabaseCreator
{
    private const string HasTablesSql = """
SELECT CASE WHEN COUNT(*) = 0 THEN FALSE ELSE TRUE END
FROM pg_class AS cls
JOIN pg_namespace AS ns ON ns.oid = cls.relnamespace
WHERE cls.relkind IN ('r', 'v', 'm', 'f', 'p')
  AND ns.nspname NOT IN ('pg_catalog', 'information_schema', 'sys')
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
