using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Storage;

/// <summary>
/// Connection that never requests a PostgreSQL isolation level: Aurora DSQL pins the isolation
/// level to <c>Repeatable Read</c> and ignores (or rejects) <c>SET TRANSACTION ISOLATION LEVEL</c>.
/// </summary>
internal sealed class DsqlRelationalConnection : NpgsqlRelationalConnection
{
    public DsqlRelationalConnection(
        RelationalConnectionDependencies dependencies,
        NpgsqlDataSourceManager dataSourceManager,
        IDbContextOptions options)
        : base(dependencies, dataSourceManager, options)
    {
    }

    protected override DbTransaction ConnectionBeginTransaction(IsolationLevel isolationLevel)
        => base.ConnectionBeginTransaction(IsolationLevel.Unspecified);

    protected override ValueTask<DbTransaction> ConnectionBeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
        => base.ConnectionBeginTransactionAsync(IsolationLevel.Unspecified, cancellationToken);
}
