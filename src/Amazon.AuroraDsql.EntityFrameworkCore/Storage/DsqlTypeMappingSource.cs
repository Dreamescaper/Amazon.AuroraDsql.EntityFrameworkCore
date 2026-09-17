using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Storage;

/// <summary>
/// Maps <em>stored</em> primitive collections to a <c>jsonb</c> (or <c>json</c>) column holding a
/// JSON array, because Aurora DSQL cannot store PostgreSQL array types. The query side is handled by
/// efcore.pg, which expands a JSON-mapped collection with
/// <c>jsonb_array_elements_text(...) WITH ORDINALITY</c>.
/// </summary>
/// <remarks>
/// Inline collection <em>parameters</em> are deliberately left as native PostgreSQL arrays: DSQL
/// supports arrays at query runtime, and operator translations such as <c>= ANY(@p)</c> and
/// <c>array_remove(@p, NULL)</c> require a real array on the right-hand side.
/// </remarks>
internal sealed class DsqlTypeMappingSource : NpgsqlTypeMappingSource
{
    [ThreadStatic]
    private static bool _mappingProperty;

    public DsqlTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies,
        ISqlGenerationHelper sqlGenerationHelper,
        INpgsqlSingletonOptions options)
        : base(dependencies, relationalDependencies, sqlGenerationHelper, options)
    {
    }

    public override RelationalTypeMapping? FindMapping(IProperty property)
    {
        // Only property (stored column) collections map to jsonb. Parameters are mapped by
        // FindMapping(Type, ...), which does not pass through here, so they keep Npgsql's native
        // array mapping.
        var previous = _mappingProperty;
        _mappingProperty = true;
        try
        {
            return base.FindMapping(property);
        }
        finally
        {
            _mappingProperty = previous;
        }
    }

    public override RelationalTypeMapping? FindCollectionMapping(
        string? storeType,
        Type? modelClrType,
        Type? providerClrType,
        CoreTypeMapping? elementMapping)
    {
        if (!_mappingProperty)
        {
            return base.FindCollectionMapping(storeType, modelClrType, providerClrType, elementMapping);
        }

        if (modelClrType is not null
            && modelClrType != typeof(byte[])
            && storeType is null or "json" or "jsonb"
            && GetElementType(modelClrType) is { } elementType
            && TryFindJsonCollectionMapping(
                new RelationalTypeMappingInfo(modelClrType).CoreTypeMappingInfo,
                modelClrType,
                providerClrType,
                ref elementMapping,
                out var comparer,
                out var collectionReaderWriter))
        {
            var jsonTypeMapping = new NpgsqlJsonTypeMapping(storeType ?? "jsonb", typeof(string));

            return (RelationalTypeMapping)jsonTypeMapping.WithComposedConverter(
                (ValueConverter)Activator.CreateInstance(
                    typeof(CollectionToJsonStringConverter<>).MakeGenericType(elementType),
                    collectionReaderWriter!)!,
                comparer,
                comparer,
                elementMapping,
                collectionReaderWriter);
        }

        return base.FindCollectionMapping(storeType, modelClrType, providerClrType, elementMapping);
    }

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType
                && @interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return @interface.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
