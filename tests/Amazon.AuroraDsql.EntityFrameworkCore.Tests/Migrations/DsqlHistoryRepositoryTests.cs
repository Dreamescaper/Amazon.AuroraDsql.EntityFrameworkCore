using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Amazon.AuroraDsql.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Migrations;

public class DsqlHistoryRepositoryTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class HistoryContext : DbContext
    {
        public HistoryContext(DbContextOptions<HistoryContext> options)
            : base(options)
        {
        }
    }

    private HistoryContext CreateContext()
        => new(new DbContextOptionsBuilder<HistoryContext>().UseDsql(_dataSource).Options);

    [Fact]
    public void History_repository_does_not_lock()
    {
        using var context = CreateContext();
        var repository = context.GetService<IHistoryRepository>();

        Assert.IsType<DsqlHistoryRepository>(repository);
        Assert.Equal(LockReleaseBehavior.Explicit, repository.LockReleaseBehavior);

        // Acquiring the lock must not issue LOCK TABLE (and therefore must not need a connection).
        using var databaseLock = repository.AcquireDatabaseLock();
    }

    [Fact]
    public void Create_script_has_no_lock_table()
    {
        using var context = CreateContext();
        var repository = context.GetService<IHistoryRepository>();

        Assert.DoesNotContain("LOCK TABLE", repository.GetCreateScript());
    }
}
