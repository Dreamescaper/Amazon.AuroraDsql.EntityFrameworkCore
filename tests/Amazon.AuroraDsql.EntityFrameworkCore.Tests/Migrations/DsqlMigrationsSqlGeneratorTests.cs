using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Migrations;

public class DsqlMigrationsSqlGeneratorTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class MigrationsContext : DbContext
    {
        public MigrationsContext(DbContextOptions<MigrationsContext> options)
            : base(options)
        {
        }

        public DbSet<Widget> Widgets => Set<Widget>();
    }

    private sealed class Widget
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    private MigrationsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MigrationsContext>()
            .UseDsql(_dataSource, dsql => dsql.EnableIdentityColumns())
            .Options;

        return new MigrationsContext(options);
    }

    private static IReadOnlyList<MigrationCommand> Generate(MigrationsContext context, params MigrationOperation[] operations)
        => context.GetService<IMigrationsSqlGenerator>().Generate(operations, context.Model);

    [Fact]
    public void Create_index_uses_async()
    {
        using var context = CreateContext();

        var commands = Generate(
            context,
            new CreateIndexOperation
            {
                Name = "IX_Widgets_Name",
                Table = "Widgets",
                Columns = ["Name"],
            });

        var sql = string.Join("\n", commands.Select(c => c.CommandText));
        Assert.Contains("CREATE INDEX ASYNC", sql);
    }

    [Fact]
    public void Unique_index_uses_async()
    {
        using var context = CreateContext();

        var commands = Generate(
            context,
            new CreateIndexOperation
            {
                Name = "IX_Widgets_Name",
                Table = "Widgets",
                Columns = ["Name"],
                IsUnique = true,
            });

        var sql = string.Join("\n", commands.Select(c => c.CommandText));
        Assert.Contains("CREATE UNIQUE INDEX ASYNC", sql);
    }

    [Fact]
    public void Concurrent_index_is_rejected()
    {
        using var context = CreateContext();

        var operation = new CreateIndexOperation
        {
            Name = "IX_Widgets_Name",
            Table = "Widgets",
            Columns = ["Name"],
        };
        operation["Npgsql:CreatedConcurrently"] = true;

        var exception = Assert.Throws<InvalidOperationException>(() => Generate(context, operation));
        Assert.Contains("CONCURRENTLY", exception.Message);
    }
}
