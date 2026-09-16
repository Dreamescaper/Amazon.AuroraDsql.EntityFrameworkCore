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
    public void Transaction_factory_is_dsql_factory()
    {
        using var context = CreateContext();

        Assert.IsType<DsqlRelationalTransactionFactory>(context.GetService<IRelationalTransactionFactory>());
    }
}
