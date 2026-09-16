using Microsoft.EntityFrameworkCore;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Extensions;

/// <summary>
/// Helpers for running explicit transactions with Aurora DSQL OCC retry.
/// </summary>
public static class DsqlTransactionExtensions
{
    /// <summary>
    /// Executes <paramref name="operation" /> inside a transaction, retrying on OCC conflicts
    /// (SQLSTATE <c>40001</c>). The change tracker is cleared at the start of every attempt so a
    /// retry does not replay stale tracked entities.
    /// </summary>
    public static Task ExecuteInTransactionAsync(
        this DbContext context,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = context.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(
            async ct =>
            {
                context.ChangeTracker.Clear();

                await using var transaction = await context.Database
                    .BeginTransactionAsync(ct)
                    .ConfigureAwait(false);

                await operation(ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            },
            cancellationToken);
    }
}
