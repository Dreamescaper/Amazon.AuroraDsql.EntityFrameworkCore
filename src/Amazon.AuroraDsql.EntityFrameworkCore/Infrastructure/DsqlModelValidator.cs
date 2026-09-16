using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Rejects model features that Aurora DSQL does not support, failing at model-validation time
/// with an actionable message instead of emitting SQL that fails at the server.
/// </summary>
internal sealed class DsqlModelValidator : NpgsqlModelValidator
{
    private static readonly HashSet<string> SupportedStoreTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "smallint", "integer", "bigint", "real", "double precision", "numeric",
        "character", "character varying", "bpchar", "text",
        "date", "time without time zone", "time with time zone",
        "timestamp without time zone", "timestamp with time zone", "interval",
        "boolean", "bytea", "uuid", "json", "jsonb",
    };

    private static readonly HashSet<string> NonIndexableStoreTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "jsonb", "bytea", "time with time zone", "interval",
    };

    public DsqlModelValidator(
        ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies,
        INpgsqlSingletonOptions npgsqlSingletonOptions)
        : base(dependencies, relationalDependencies, npgsqlSingletonOptions)
    {
    }

    public override void Validate(
        IModel model,
        IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        base.Validate(model, logger);

        ValidateExtensions(model);
        ValidateProperties(model);
        ValidateIndexes(model);
    }

    private static void ValidateExtensions(IModel model)
    {
        var extensions = model.GetPostgresExtensions().Select(e => e.Name).ToList();
        if (extensions.Count > 0)
        {
            throw new InvalidOperationException(
                $"Aurora DSQL does not support PostgreSQL extensions, but the model configures: "
                + $"{string.Join(", ", extensions)}. Remove HasPostgresExtension/UsePostgresExtension calls.");
        }
    }

    private static void ValidateProperties(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                var storeType = property.GetRelationalTypeMapping().StoreTypeNameBase;
                if (!SupportedStoreTypes.Contains(storeType))
                {
                    throw new InvalidOperationException(
                        $"Aurora DSQL does not support the column type '{storeType}' used by "
                        + $"{entityType.DisplayName()}.{property.Name}. Map it to a supported type, "
                        + "or use a value converter (e.g. an enum to integer/text).");
                }
            }
        }
    }

    private static void ValidateIndexes(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var index in entityType.GetIndexes())
            {
                foreach (var property in index.Properties)
                {
                    var storeType = property.GetRelationalTypeMapping().StoreTypeNameBase;
                    if (NonIndexableStoreTypes.Contains(storeType))
                    {
                        throw new InvalidOperationException(
                            $"Aurora DSQL cannot index columns of type '{storeType}' "
                            + $"({entityType.DisplayName()}.{property.Name}). Remove this index.");
                    }
                }
            }
        }
    }
}
