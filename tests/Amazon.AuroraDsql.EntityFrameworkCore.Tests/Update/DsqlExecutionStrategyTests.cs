using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Amazon.AuroraDsql.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DsqlOptionsExtension = Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure.DsqlOptionsExtension;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Update;

public class DsqlExecutionStrategyTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class StrategyContext : DbContext
    {
        public StrategyContext(DbContextOptions<StrategyContext> options)
            : base(options)
        {
        }
    }

    [Fact]
    public void Execution_strategy_is_dsql()
    {
        var options = new DbContextOptionsBuilder<StrategyContext>().UseDsql(_dataSource).Options;
        using var context = new StrategyContext(options);

        Assert.IsType<DsqlExecutionStrategy>(context.Database.CreateExecutionStrategy());
    }

    [Fact]
    public void Occ_sqlstate_is_recognised()
    {
        var exception = new PostgresException("conflict", "ERROR", "ERROR", "40001");

        Assert.True(DsqlExecutionStrategy.IsOccConflict(exception));
    }

    [Fact]
    public void Other_sqlstates_are_not_occ()
    {
        var exception = new PostgresException("unique violation", "ERROR", "ERROR", "23505");

        Assert.False(DsqlExecutionStrategy.IsOccConflict(exception));
    }

    [Fact]
    public void Retry_options_propagate_to_the_extension()
    {
        var builder = new DbContextOptionsBuilder<StrategyContext>()
            .UseDsql(_dataSource, dsql => dsql.SetMaxRetryCount(3).SetMaxRetryDelay(TimeSpan.FromSeconds(5)));

        var extension = builder.Options.FindExtension<DsqlOptionsExtension>()!;

        Assert.Equal(3, extension.MaxRetryCount);
        Assert.Equal(TimeSpan.FromSeconds(5), extension.MaxRetryDelay);
    }
}
