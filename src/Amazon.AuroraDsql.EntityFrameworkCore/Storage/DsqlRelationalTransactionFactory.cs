using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Storage;

/// <summary>
/// Creates transactions that do not support savepoints, so EF never wraps <c>SaveChanges</c> in a
/// <c>SAVEPOINT</c> (which Aurora DSQL does not support).
/// </summary>
internal sealed class DsqlRelationalTransactionFactory : RelationalTransactionFactory
{
    public DsqlRelationalTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
        : base(dependencies)
    {
    }

    public override RelationalTransaction Create(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned)
        => new DsqlRelationalTransaction(
            connection,
            transaction,
            transactionId,
            logger,
            transactionOwned,
            Dependencies.SqlGenerationHelper);
}

internal sealed class DsqlRelationalTransaction : RelationalTransaction
{
    public DsqlRelationalTransaction(
        IRelationalConnection connection,
        DbTransaction transaction,
        Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
        bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper)
        : base(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {
    }

    public override bool SupportsSavepoints => false;
}
