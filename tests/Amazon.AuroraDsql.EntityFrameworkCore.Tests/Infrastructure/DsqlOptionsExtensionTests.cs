using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Infrastructure;

public class DsqlOptionsExtensionTests
{
    private sealed class TestContext : DbContext
    {
        public TestContext(DbContextOptions<TestContext> options)
            : base(options)
        {
        }
    }

    private static NpgsqlDataSource CreateDataSource()
        => new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    [Fact]
    public void UseDsql_registers_npgsql_provider_and_dsql_extension()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestContext>();

        using var dataSource = CreateDataSource();
        optionsBuilder.UseDsql(dataSource);

        var options = optionsBuilder.Options;
        Assert.Contains(options.Extensions, e => e.Info.IsDatabaseProvider);
        Assert.NotNull(options.FindExtension<DsqlOptionsExtension>());
    }

    [Fact]
    public void UseDsql_invokes_options_action()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestContext>();
        var invoked = false;

        using var dataSource = CreateDataSource();
        optionsBuilder.UseDsql(dataSource, _ => invoked = true);

        Assert.True(invoked);
    }

    [Fact]
    public void UseDsql_replaces_rather_than_duplicates_extension()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestContext>();

        using var dataSource = CreateDataSource();
        optionsBuilder.UseDsql(dataSource);
        optionsBuilder.UseDsql(dataSource);

        Assert.Single(optionsBuilder.Options.Extensions.OfType<DsqlOptionsExtension>());
    }

    [Fact]
    public void UseDsql_logs_aurora_dsql_fragment()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestContext>();

        using var dataSource = CreateDataSource();
        optionsBuilder.UseDsql(dataSource);

        var info = optionsBuilder.Options.FindExtension<DsqlOptionsExtension>()!.Info;
        Assert.Contains("AuroraDsql", info.LogFragment);
        Assert.False(info.IsDatabaseProvider);
    }
}
