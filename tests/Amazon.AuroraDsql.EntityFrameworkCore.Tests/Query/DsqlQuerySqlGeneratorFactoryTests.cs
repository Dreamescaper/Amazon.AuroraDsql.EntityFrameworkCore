using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Query;

public class DsqlQuerySqlGeneratorFactoryTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class QueryContext : DbContext
    {
        public QueryContext(DbContextOptions<QueryContext> options)
            : base(options)
        {
        }

        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class Item
    {
        public Guid Id { get; set; }
        public string? Label { get; set; }
    }

    [Fact]
    public void Nulls_first_is_emitted_when_enabled()
    {
        using var context = CreateContext(nullsFirst: true);

        var sql = context.Items.OrderBy(i => i.Label).ToQueryString();

        Assert.Contains("NULLS FIRST", sql);
    }

    [Fact]
    public void Nulls_first_is_not_emitted_by_default()
    {
        using var context = CreateContext(nullsFirst: false);

        var sql = context.Items.OrderBy(i => i.Label).ToQueryString();

        Assert.DoesNotContain("NULLS FIRST", sql);
    }

    private QueryContext CreateContext(bool nullsFirst)
    {
        var options = new DbContextOptionsBuilder<QueryContext>()
            .UseDsql(_dataSource, dsql =>
            {
                if (nullsFirst)
                {
                    dsql.NullsFirst();
                }
            })
            .Options;

        return new QueryContext(options);
    }
}
