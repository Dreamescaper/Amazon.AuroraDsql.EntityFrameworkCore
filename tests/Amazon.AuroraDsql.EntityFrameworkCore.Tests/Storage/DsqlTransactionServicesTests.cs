using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Amazon.AuroraDsql.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Storage;

public class DsqlTransactionServicesTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class TransactionContext : DbContext
    {
        public TransactionContext(DbContextOptions<TransactionContext> options)
            : base(options)
        {
        }
    }

    private TransactionContext CreateContext()
        => new(new DbContextOptionsBuilder<TransactionContext>().UseDsql(_dataSource).Options);

    [Fact]
    public void Connection_is_dsql_connection()
    {
        using var context = CreateContext();

        Assert.IsType<DsqlRelationalConnection>(context.GetService<IRelationalConnection>());
    }

    [Fact]
    public void Database_always_exists_without_a_connection()
    {
        using var context = CreateContext();

        // DSQL's single database always exists; this must not hit pg_database.
        Assert.True(context.GetService<IRelationalDatabaseCreator>().Exists());
    }

    [Fact]
    public void Database_creator_is_dsql_creator()
    {
        using var context = CreateContext();

        Assert.IsType<DsqlDatabaseCreator>(context.GetService<IRelationalDatabaseCreator>());
    }

    [Fact]
    public void Transaction_factory_is_dsql_factory()
    {
        using var context = CreateContext();

        Assert.IsType<DsqlRelationalTransactionFactory>(context.GetService<IRelationalTransactionFactory>());
    }
}
