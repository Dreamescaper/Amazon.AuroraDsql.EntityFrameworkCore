using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Tests.Storage;

public class DsqlTypeMappingSourceTests : IDisposable
{
    private readonly NpgsqlDataSource _dataSource =
        new NpgsqlDataSourceBuilder("Host=localhost;Username=admin;Database=postgres").Build();

    public void Dispose() => _dataSource.Dispose();

    private sealed class CollectionsContext : DbContext
    {
        public CollectionsContext(DbContextOptions<CollectionsContext> options)
            : base(options)
        {
        }

        public DbSet<CollectionsEntity> Entities => Set<CollectionsEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CollectionsEntity>().Property(e => e.JsonNumbers).HasColumnType("json");
    }

    private sealed class CollectionsEntity
    {
        public Guid Id { get; set; }
        public int[] Numbers { get; set; } = [];
        public List<string> Tags { get; set; } = [];
        public byte[] Data { get; set; } = [];
        public List<int> JsonNumbers { get; set; } = [];
        public int Quantity { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    private CollectionsContext CreateContext()
        => new(new DbContextOptionsBuilder<CollectionsContext>().UseDsql(_dataSource).Options);

    private static Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping Mapping(CollectionsContext context, string property)
        => context.Model.FindEntityType(typeof(CollectionsEntity))!.FindProperty(property)!.GetRelationalTypeMapping();

    [Fact]
    public void Array_maps_to_jsonb()
    {
        using var context = CreateContext();
        var mapping = Mapping(context, nameof(CollectionsEntity.Numbers));

        Assert.Equal("jsonb", mapping.StoreType);
        Assert.NotNull(mapping.ElementTypeMapping);
        Assert.NotNull(mapping.Converter);
    }

    [Fact]
    public void List_maps_to_jsonb()
    {
        using var context = CreateContext();
        var mapping = Mapping(context, nameof(CollectionsEntity.Tags));

        Assert.Equal("jsonb", mapping.StoreType);
    }

    [Fact]
    public void Explicit_json_store_type_is_honoured()
    {
        using var context = CreateContext();
        var mapping = Mapping(context, nameof(CollectionsEntity.JsonNumbers));

        Assert.Equal("json", mapping.StoreType);
    }

    [Fact]
    public void Byte_array_stays_bytea()
    {
        using var context = CreateContext();
        var mapping = Mapping(context, nameof(CollectionsEntity.Data));

        Assert.Equal("bytea", mapping.StoreType);
    }

    [Fact]
    public void Inline_int_array_parameter_stays_a_native_array()
    {
        using var context = CreateContext();
        var ids = new[] { 1, 2 };

        var sql = context.Entities.Where(e => ids.Contains(e.Quantity)).ToQueryString();

        Assert.Contains("= ANY", sql);
        Assert.DoesNotContain("jsonb", sql);
    }

    [Fact]
    public void Inline_string_array_parameter_stays_a_native_array()
    {
        using var context = CreateContext();
        var names = new[] { "a", "b" };

        var sql = context.Entities.Where(e => names.Contains(e.Label)).ToQueryString();

        Assert.Contains("= ANY", sql);
        Assert.DoesNotContain("jsonb", sql);
    }

    [Fact]
    public void Contains_uses_jsonb_containment()
    {
        using var context = CreateContext();

        var sql = context.Entities.Where(e => e.Numbers.Contains(5)).ToQueryString();

        Assert.Contains("@> to_jsonb(5)", sql);
    }

    [Fact]
    public void Querying_elements_uses_jsonb_array_elements()
    {
        using var context = CreateContext();

        var sql = context.Entities.Where(e => e.Numbers.Any(n => n > 3)).ToQueryString();

        Assert.Contains("jsonb_array_elements_text", sql);
    }
}
