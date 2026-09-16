using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Storage;

/// <summary>
/// Connection that always requests Aurora DSQL's fixed <c>Repeatable Read</c> isolation level.
/// Npgsql maps <see cref="IsolationLevel.Unspecified" /> to an explicit <c>READ COMMITTED</c>,
/// which DSQL rejects, so any requested level is overridden.
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
        => base.ConnectionBeginTransaction(IsolationLevel.RepeatableRead);

    protected override ValueTask<DbTransaction> ConnectionBeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
        => base.ConnectionBeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
}
