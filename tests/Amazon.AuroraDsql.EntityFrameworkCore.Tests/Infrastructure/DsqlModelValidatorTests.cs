using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Infrastructure;

public class DsqlModelValidatorTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private DbContextOptions<T> Options<T>()
        where T : DbContext
        => new DbContextOptionsBuilder<T>().UseDsql(_dataSource).Options;

    private sealed class SupportedContext : DbContext
    {
        public SupportedContext(DbContextOptions<SupportedContext> options)
            : base(options)
        {
        }

        public DbSet<SupportedEntity> Entities => Set<SupportedEntity>();
    }

    private sealed class SupportedEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Amount { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class InetContext : DbContext
    {
        public InetContext(DbContextOptions<InetContext> options)
            : base(options)
        {
        }

        public DbSet<InetEntity> Entities => Set<InetEntity>();
    }

    private sealed class InetEntity
    {
        public Guid Id { get; set; }
        public System.Net.IPAddress Address { get; set; } = System.Net.IPAddress.Loopback;
    }

    private sealed class HstoreContext : DbContext
    {
        public HstoreContext(DbContextOptions<HstoreContext> options)
            : base(options)
        {
        }

        public DbSet<HstoreEntity> Entities => Set<HstoreEntity>();
    }

    private sealed class HstoreEntity
    {
        public Guid Id { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
    }

    private sealed class RangeContext : DbContext
    {
        public RangeContext(DbContextOptions<RangeContext> options)
            : base(options)
        {
        }

        public DbSet<RangeEntity> Entities => Set<RangeEntity>();
    }

    private sealed class RangeEntity
    {
        public Guid Id { get; set; }
        public NpgsqlRange<int> Span { get; set; }
    }

    private sealed class AliasContext : DbContext
    {
        public AliasContext(DbContextOptions<AliasContext> options)
            : base(options)
        {
        }

        public DbSet<AliasEntity> Entities => Set<AliasEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AliasEntity>(builder =>
            {
                builder.Property(e => e.Count).HasColumnType("int");
                builder.Property(e => e.Amount).HasColumnType("decimal(18,6)");
                builder.Property(e => e.Name).HasColumnType("varchar(100)");
                builder.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            });
        }
    }

    private sealed class AliasEntity
    {
        public Guid Id { get; set; }
        public int Count { get; set; }
        public decimal Amount { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ShortIdentityContext : DbContext
    {
        public ShortIdentityContext(DbContextOptions<ShortIdentityContext> options)
            : base(options)
        {
        }

        public DbSet<ShortIdentityEntity> Entities => Set<ShortIdentityEntity>();
    }

    private sealed class ShortIdentityEntity
    {
        public short Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonIndexContext : DbContext
    {
        public JsonIndexContext(DbContextOptions<JsonIndexContext> options)
            : base(options)
        {
        }

        public DbSet<JsonIndexEntity> Entities => Set<JsonIndexEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<JsonIndexEntity>(builder =>
            {
                builder.Property(e => e.Payload).HasColumnType("jsonb");
                builder.HasIndex(e => e.Payload);
            });
    }

    private sealed class JsonIndexEntity
    {
        public Guid Id { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    [Fact]
    public void Supported_model_validates()
    {
        using var context = new SupportedContext(Options<SupportedContext>());

        Assert.NotNull(context.Model);
    }

    [Fact]
    public void Non_bigint_identity_column_is_rejected()
    {
        using var context = new ShortIdentityContext(Options<ShortIdentityContext>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("bigint", exception.Message);
    }

    [Fact]
    public void Postgres_type_aliases_are_accepted()
    {
        using var context = new AliasContext(Options<AliasContext>());

        Assert.NotNull(context.Model);
    }

    [Fact]
    public void Inet_column_is_rejected()
    {
        using var context = new InetContext(Options<InetContext>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("inet", exception.Message);
    }

    [Fact]
    public void Hstore_column_is_rejected()
    {
        using var context = new HstoreContext(Options<HstoreContext>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("hstore", exception.Message);
    }

    [Fact]
    public void Range_column_is_rejected()
    {
        using var context = new RangeContext(Options<RangeContext>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("range", exception.Message);
    }

    [Fact]
    public void Index_on_jsonb_column_is_rejected()
    {
        using var context = new JsonIndexContext(Options<JsonIndexContext>());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("jsonb", exception.Message);
    }
}
