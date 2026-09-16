using System.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

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

    private static int Execute(
        IReadOnlyList<MigrationCommand> commands,
        IRelationalConnection connection,
        MigrationExecutionState executionState)
    {
        var result = 0;
        var connectionOpened = connection.Open();

        try
        {
            for (var i = executionState.LastCommittedCommandIndex; i < commands.Count; i++)
            {
                result = commands[i].ExecuteNonQuery(connection);
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

    private static async Task<int> ExecuteAsync(
        IReadOnlyList<MigrationCommand> commands,
        IRelationalConnection connection,
        MigrationExecutionState executionState,
        CancellationToken cancellationToken)
    {
        var result = 0;
        var connectionOpened = await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            for (var i = executionState.LastCommittedCommandIndex; i < commands.Count; i++)
            {
                result = await commands[i].ExecuteNonQueryAsync(connection, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
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
