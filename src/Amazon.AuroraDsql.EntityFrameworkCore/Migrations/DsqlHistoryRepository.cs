using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Migrations;

/// <summary>
/// History repository that does not take a <c>LOCK TABLE ... ACCESS EXCLUSIVE</c> on the migrations
/// history table, which Aurora DSQL does not support (DSQL is lock-free and uses OCC).
/// </summary>
/// <remarks>
/// Consequence: concurrent migration application is not serialized by a database lock. Run
/// migrations from a single runner, or rely on the OCC execution strategy to retry conflicts.
/// </remarks>
internal sealed class DsqlHistoryRepository : NpgsqlHistoryRepository
{
    public DsqlHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies)
    {
    }

    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
        => new NoopDatabaseLock(this);

    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IMigrationsDatabaseLock>(new NoopDatabaseLock(this));

    private sealed class NoopDatabaseLock : IMigrationsDatabaseLock
    {
        private readonly IHistoryRepository _historyRepository;

        public NoopDatabaseLock(IHistoryRepository historyRepository)
            => _historyRepository = historyRepository;

        IHistoryRepository IMigrationsDatabaseLock.HistoryRepository => _historyRepository;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => default;
    }
}
