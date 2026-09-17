using Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Migrations;

/// <summary>
/// Generates Aurora DSQL-compatible DDL. Aurora DSQL has no synchronous <c>CREATE INDEX</c>;
/// indexes are created asynchronously with <c>CREATE INDEX ASYNC</c>.
/// </summary>
internal sealed class DsqlMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
{
    private readonly DsqlOptionsExtension _options;

    private ISqlGenerationHelper SqlGenerationHelper => Dependencies.SqlGenerationHelper;

    public DsqlMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        INpgsqlSingletonOptions npgsqlSingletonOptions,
        IDbContextOptions contextOptions)
        : base(dependencies, npgsqlSingletonOptions)
        => _options = contextOptions.FindExtension<DsqlOptionsExtension>() ?? new DsqlOptionsExtension();

    protected override void ColumnDefinition(
        string? schema,
        string table,
        string name,
        ColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        ApplyIdentityCache(operation);
        base.ColumnDefinition(schema, table, name, operation, model, builder);
    }

    protected override void Generate(
        AlterColumnOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        var oldType = operation.OldColumn?.ColumnType;
        var newType = operation.ColumnType;

        if (oldType is not null
            && newType is not null
            && !string.Equals(oldType, newType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Aurora DSQL does not support ALTER COLUMN ... TYPE ('{oldType}' -> '{newType}'). "
                + "Add a new column and migrate the data, or drop and re-add the column.");
        }

        base.Generate(operation, model, builder);
    }

    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // A foreign key added to an existing table must be added NOT VALID, then validated
        // asynchronously as a separate statement.
        builder
            .Append("ALTER TABLE ")
            .Append(SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" ADD ");

        ForeignKeyConstraint(operation, model, builder);
        builder.Append(" NOT VALID");

        if (terminate)
        {
            builder.AppendLine(";");
            EndStatement(builder);
        }

        if (operation.Name is not null)
        {
            builder
                .Append("ALTER TABLE ASYNC ")
                .Append(SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
                .Append(" VALIDATE CONSTRAINT ")
                .Append(SqlGenerationHelper.DelimitIdentifier(operation.Name))
                .AppendLine(";");
            EndStatement(builder);
        }
    }

    protected override void IdentityDefinition(
        ColumnOperation operation,
        MigrationCommandListBuilder builder)
    {
        var identityOptions = operation[NpgsqlAnnotationNames.IdentityOptions] as string;

        base.IdentityDefinition(operation, builder);

        // Npgsql omits CACHE when it is 1; DSQL requires an explicit cache size. Append one when
        // the injected options carry nothing but the cache.
        if (identityOptions is not null)
        {
            var data = IdentitySequenceOptionsData.Deserialize(identityOptions);

            if (data is { NumbersToCache: 1, StartValue: null, IncrementBy: 1, MinValue: null, MaxValue: null, IsCyclic: false })
            {
                builder.Append(" (CACHE 1)");
            }
        }
    }

    private void ApplyIdentityCache(ColumnOperation operation)
    {
        // DSQL rejects identity columns that do not specify an explicit cache. When caching is not
        // opted into, emit CACHE 1 (DSQL's other permitted value).
        if (operation[NpgsqlAnnotationNames.IdentityOptions] is not null
            || operation[NpgsqlAnnotationNames.ValueGenerationStrategy] is not NpgsqlValueGenerationStrategy strategy
            || strategy is not (NpgsqlValueGenerationStrategy.IdentityAlwaysColumn
                or NpgsqlValueGenerationStrategy.IdentityByDefaultColumn))
        {
            return;
        }

        var cacheSize = _options.UseIdentityColumns ? _options.IdentityCacheSize : 1;

        operation[NpgsqlAnnotationNames.IdentityOptions] =
            new IdentitySequenceOptionsData { NumbersToCache = cacheSize }.Serialize();
    }

    protected override void Generate(
        CreateIndexOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        if (operation[NpgsqlAnnotationNames.CreatedConcurrently] as bool? == true)
        {
            throw new InvalidOperationException(
                "Aurora DSQL does not support CREATE INDEX CONCURRENTLY. "
                + "Remove the concurrently annotation; DSQL indexes are created asynchronously "
                + "with CREATE INDEX ASYNC.");
        }

        builder.Append("CREATE ");

        if (operation.IsUnique)
        {
            builder.Append("UNIQUE ");
        }

        builder.Append("INDEX ASYNC ")
            .Append(SqlGenerationHelper.DelimitIdentifier(operation.Name))
            .Append(" ON ")
            .Append(SqlGenerationHelper.DelimitIdentifier(operation.Table, operation.Schema))
            .Append(" (")
            .Append(string.Join(", ", operation.Columns.Select(SqlGenerationHelper.DelimitIdentifier)))
            .Append(")");

        if (!string.IsNullOrEmpty(operation.Filter))
        {
            builder.Append(" WHERE ").Append(operation.Filter);
        }

        if (terminate)
        {
            builder.AppendLine(";");
            EndStatement(builder);
        }
    }
}
