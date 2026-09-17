using System.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Migrations;

/// <summary>
/// Executes each migration command in its own implicit transaction. Aurora DSQL allows only one DDL
/// statement per transaction, so a migration's statements cannot share a wrapping transaction.
/// </summary>
/// <remarks>
/// OCC retry around migration execution is not applied here yet; see the plan (Phase 5 / deferred).
/// </remarks>
internal sealed class DsqlMigrationCommandExecutor : IMigrationCommandExecutor
{
    // Statements that have no IF NOT EXISTS form (ADD CONSTRAINT) still run against an object a
    // previous, partially-applied run created; treat the duplicate as already applied.
    private static readonly string[] AlreadyAppliedSqlStates = ["42710", "42P07", "42701"];

    private readonly ILogger _logger;

    public DsqlMigrationCommandExecutor(ILoggerFactory loggerFactory)
        => _logger = loggerFactory.CreateLogger<DsqlMigrationCommandExecutor>();

    public void ExecuteNonQuery(
        IEnumerable<MigrationCommand> migrationCommands,
        IRelationalConnection connection)
        => ExecuteNonQuery(
            migrationCommands.ToList(),
            connection,
            new MigrationExecutionState(),
            commitTransaction: true);

    public int ExecuteNonQuery(
        IReadOnlyList<MigrationCommand> migrationCommands,
        IRelationalConnection connection,
        MigrationExecutionState executionState,
        bool commitTransaction,
        IsolationLevel? isolationLevel = null)
        => Execute(migrationCommands, connection, executionState);

    public Task ExecuteNonQueryAsync(
        IEnumerable<MigrationCommand> migrationCommands,
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
        => ExecuteNonQueryAsync(
            migrationCommands.ToList(),
            connection,
            new MigrationExecutionState(),
            commitTransaction: true,
            cancellationToken: cancellationToken);

    public async Task<int> ExecuteNonQueryAsync(
        IReadOnlyList<MigrationCommand> migrationCommands,
        IRelationalConnection connection,
        MigrationExecutionState executionState,
        bool commitTransaction,
        IsolationLevel? isolationLevel = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(migrationCommands, connection, executionState, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// EF's <c>Migrator</c> opens one transaction around the whole migration. DSQL allows only one
    /// DDL statement per transaction, so commit and drop that transaction and let each command
    /// autocommit. The migrator's final <c>Transaction?.Commit()</c> is a no-op on the null value.
    /// </summary>
    private static void ReleaseAmbientTransaction(MigrationExecutionState executionState)
    {
        if (executionState.Transaction is not null)
        {
            executionState.Transaction.Commit();
            executionState.Transaction.Dispose();
            executionState.Transaction = null;
        }
    }

    private static bool AlreadyApplied(PostgresException exception)
        => AlreadyAppliedSqlStates.Contains(exception.SqlState);

    private void LogAlreadyApplied(PostgresException exception)
        => _logger.LogInformation(
            "Ignoring already-applied migration statement (SQLSTATE {SqlState}): {Message}",
            exception.SqlState,
            exception.MessageText);

    private int Execute(
        IReadOnlyList<MigrationCommand> commands,
        IRelationalConnection connection,
        MigrationExecutionState executionState)
    {
        var result = 0;
        var connectionOpened = connection.Open();
        ReleaseAmbientTransaction(executionState);

        try
        {
            for (var i = executionState.LastCommittedCommandIndex; i < commands.Count; i++)
            {
                try
                {
                    result = commands[i].ExecuteNonQuery(connection);
                }
                catch (PostgresException exception) when (AlreadyApplied(exception))
                {
                    LogAlreadyApplied(exception);
                }

                executionState.LastCommittedCommandIndex = i + 1;
                executionState.AnyOperationPerformed = true;
            }
        }
        finally
        {
            if (connectionOpened)
            {
                connection.Close();
            }
        }

        return result;
    }

    private async Task<int> ExecuteAsync(
        IReadOnlyList<MigrationCommand> commands,
        IRelationalConnection connection,
        MigrationExecutionState executionState,
        CancellationToken cancellationToken)
    {
        var result = 0;
        var connectionOpened = await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        ReleaseAmbientTransaction(executionState);

        try
        {
            for (var i = executionState.LastCommittedCommandIndex; i < commands.Count; i++)
            {
                try
                {
                    result = await commands[i].ExecuteNonQueryAsync(connection, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (PostgresException exception) when (AlreadyApplied(exception))
                {
                    LogAlreadyApplied(exception);
                }

                executionState.LastCommittedCommandIndex = i + 1;
                executionState.AnyOperationPerformed = true;
            }
        }
        finally
        {
            if (connectionOpened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }

        return result;
    }
}
