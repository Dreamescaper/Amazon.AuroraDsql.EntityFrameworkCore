using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Migrations;

/// <summary>
/// Generates Aurora DSQL-compatible DDL. Aurora DSQL has no synchronous <c>CREATE INDEX</c>;
/// indexes are created asynchronously with <c>CREATE INDEX ASYNC</c>.
/// </summary>
internal sealed class DsqlMigrationsSqlGenerator : NpgsqlMigrationsSqlGenerator
{
    private ISqlGenerationHelper SqlGenerationHelper => Dependencies.SqlGenerationHelper;

    public DsqlMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        INpgsqlSingletonOptions npgsqlSingletonOptions)
        : base(dependencies, npgsqlSingletonOptions)
    {
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
