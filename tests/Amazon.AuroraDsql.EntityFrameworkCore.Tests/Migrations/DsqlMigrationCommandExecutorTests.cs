using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Amazon.AuroraDsql.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Migrations;

public class DsqlMigrationCommandExecutorTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class ExecutorContext : DbContext
    {
        public ExecutorContext(DbContextOptions<ExecutorContext> options)
            : base(options)
        {
        }
    }

    [Fact]
    public void Migration_command_executor_is_replaced()
    {
        var options = new DbContextOptionsBuilder<ExecutorContext>().UseDsql(_dataSource).Options;
        using var context = new ExecutorContext(options);

        Assert.IsType<DsqlMigrationCommandExecutor>(context.GetService<IMigrationCommandExecutor>());
    }
}
