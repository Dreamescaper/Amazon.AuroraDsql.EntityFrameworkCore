using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using DsqlDbContextOptionsBuilder = Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure.DsqlDbContextOptionsBuilder;
using DsqlOptionsExtension = Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure.DsqlOptionsExtension;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Metadata;

public class DsqlModelFinalizingConventionTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class GuidKeyContext : DbContext
    {
        public GuidKeyContext(DbContextOptions<GuidKeyContext> options)
            : base(options)
        {
        }

        public DbSet<GuidEntity> Entities => Set<GuidEntity>();
    }

    private sealed class GuidEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class IntKeyContext : DbContext
    {
        public IntKeyContext(DbContextOptions<IntKeyContext> options)
            : base(options)
        {
        }

        public DbSet<IntEntity> Entities => Set<IntEntity>();
    }

    private sealed class IntEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class LongKeyContext : DbContext
    {
        public LongKeyContext(DbContextOptions<LongKeyContext> options)
            : base(options)
        {
        }
    }

    private sealed class ExplicitGuidKeyContext : DbContext
    {
        public ExplicitGuidKeyContext(DbContextOptions<ExplicitGuidKeyContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<GuidEntity>().Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()");
    }

    [Fact]
    public void Guid_primary_key_gets_gen_random_uuid_default()
    {
        var options = new DbContextOptionsBuilder<GuidKeyContext>().UseDsql(_dataSource).Options;
        using var context = new GuidKeyContext(options);

        var property = context.Model.FindEntityType(typeof(GuidEntity))!.FindProperty(nameof(GuidEntity.Id))!;

        Assert.Equal("gen_random_uuid()", property.GetDefaultValueSql());
        Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
    }

    [Fact]
    public void Explicit_default_value_is_not_overridden()
    {
        var options = new DbContextOptionsBuilder<ExplicitGuidKeyContext>().UseDsql(_dataSource).Options;
        using var context = new ExplicitGuidKeyContext(options);

        var property = context.Model.FindEntityType(typeof(GuidEntity))!.FindProperty(nameof(GuidEntity.Id))!;

        Assert.Equal("uuid_generate_v4()", property.GetDefaultValueSql());
    }

    [Fact]
    public void Int_identity_key_is_widened_to_bigint()
    {
        using var context = new IntKeyContext(
            new DbContextOptionsBuilder<IntKeyContext>().UseDsql(_dataSource).Options);
        var property = context.Model.FindEntityType(typeof(IntEntity))!.FindProperty(nameof(IntEntity.Id))!;

        Assert.Equal("bigint", property.GetColumnType());
        Assert.NotNull(property.GetValueConverter());
    }

    [Fact]
    public void EnableIdentityColumns_sets_the_extension_flag()
    {
        var builder = new DbContextOptionsBuilder<LongKeyContext>()
            .UseDsql(_dataSource, dsql => dsql.EnableIdentityColumns());
        var extension = builder.Options.FindExtension<DsqlOptionsExtension>()!;

        Assert.True(extension.UseIdentityColumns);
        Assert.Equal(DsqlDbContextOptionsBuilder.DefaultIdentityCacheSize, extension.IdentityCacheSize);
    }

    [Fact]
    public void Identity_cache_size_must_be_one_or_large()
    {
        var builder = new DbContextOptionsBuilder<LongKeyContext>();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseDsql(_dataSource, dsql => dsql.EnableIdentityColumns(100)));
        Assert.NotNull(builder.UseDsql(_dataSource, dsql => dsql.EnableIdentityColumns(1)));
    }
}
