using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Metadata;

public class DsqlDecimalPrecisionConventionTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class DefaultContext : DbContext
    {
        public DefaultContext(DbContextOptions<DefaultContext> options)
            : base(options)
        {
        }

        public DbSet<Amount> Amounts => Set<Amount>();
    }

    private sealed class ExplicitContext : DbContext
    {
        public ExplicitContext(DbContextOptions<ExplicitContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Amount>().Property(a => a.Value).HasPrecision(10, 2);
    }

    private sealed class Amount
    {
        public Guid Id { get; set; }
        public decimal Value { get; set; }
        public decimal? Optional { get; set; }
    }

    [Fact]
    public void Decimal_defaults_to_numeric_18_6()
    {
        using var context = new DefaultContext(new DbContextOptionsBuilder<DefaultContext>().UseDsql(_dataSource).Options);
        var entity = context.Model.FindEntityType(typeof(Amount))!;

        var value = entity.FindProperty(nameof(Amount.Value))!;
        var optional = entity.FindProperty(nameof(Amount.Optional))!;

        Assert.Equal(18, value.GetPrecision());
        Assert.Equal(6, value.GetScale());
        Assert.Equal(18, optional.GetPrecision());
        Assert.Equal(6, optional.GetScale());
    }

    [Fact]
    public void Explicit_precision_is_respected()
    {
        using var context = new ExplicitContext(new DbContextOptionsBuilder<ExplicitContext>().UseDsql(_dataSource).Options);
        var property = context.Model.FindEntityType(typeof(Amount))!.FindProperty(nameof(Amount.Value))!;

        Assert.Equal(10, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
    }
}
