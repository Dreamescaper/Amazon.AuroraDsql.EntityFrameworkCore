using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Update;

/// <summary>
/// Execution strategy that retries Aurora DSQL optimistic-concurrency (OCC) failures. DSQL reports
/// conflicts with SQLSTATE <c>40001</c>, which Npgsql's transient detection does not cover.
/// </summary>
internal sealed class DsqlExecutionStrategy : NpgsqlRetryingExecutionStrategy
{
    public DsqlExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount,
        TimeSpan maxRetryDelay)
        : base(dependencies, maxRetryCount, maxRetryDelay, errorCodesToAdd: null)
    {
    }

    protected override bool ShouldRetryOn(Exception? exception)
        => base.ShouldRetryOn(exception) || IsOccConflict(exception);

    internal static bool IsOccConflict(Exception? exception)
        => exception is PostgresException { SqlState: "40001" };
}
