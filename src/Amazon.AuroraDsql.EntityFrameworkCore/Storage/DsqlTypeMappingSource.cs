using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Storage;

/// <summary>
/// Maps primitive collections to a <c>jsonb</c> (or <c>json</c>) column holding a JSON array,
/// because Aurora DSQL cannot store PostgreSQL array types. The query side is already handled by
/// efcore.pg, which expands a JSON-mapped collection with
/// <c>jsonb_array_elements_text(...) WITH ORDINALITY</c>.
/// </summary>
internal sealed class DsqlTypeMappingSource : NpgsqlTypeMappingSource
{
    public DsqlTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies,
        ISqlGenerationHelper sqlGenerationHelper,
        INpgsqlSingletonOptions options)
        : base(dependencies, relationalDependencies, sqlGenerationHelper, options)
    {
    }

    public override RelationalTypeMapping? FindCollectionMapping(
        string? storeType,
        Type? modelClrType,
        Type? providerClrType,
        CoreTypeMapping? elementMapping)
    {
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
