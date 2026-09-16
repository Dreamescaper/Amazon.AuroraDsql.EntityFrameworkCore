using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Metadata;

/// <summary>
/// Applies DSQL-compatible key defaults: <c>gen_random_uuid()</c> for <see cref="Guid" /> primary
/// keys. Existing explicit configuration is left untouched.
/// </summary>
/// <remarks>
/// Identity cache sizing for <see cref="long" /> keys is handled by the migrations SQL generator
/// rather than here: annotations added in a model-finalizing convention do not survive into the
/// runtime model (verified against EF Core 10 / Npgsql EF 10). See
/// <c>docs/implementation-plan.md</c> Phase 3.
/// </remarks>
internal sealed class DsqlModelFinalizingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.IsOwned())
            {
                continue;
            }

            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null)
            {
                continue;
            }

            foreach (var property in primaryKey.Properties)
            {
                ConfigureKeyProperty(property);
            }
        }
    }

    private static void ConfigureKeyProperty(IConventionProperty property)
    {
        if (property.ClrType != typeof(Guid))
        {
            return;
        }

        if (property.GetDefaultValueSql() is not null)
        {
            return;
        }

        if (property.GetValueGenerationStrategy() != NpgsqlValueGenerationStrategy.None)
        {
            return;
        }

        property.Builder.HasDefaultValueSql("gen_random_uuid()");
        property.Builder.ValueGenerated(ValueGenerated.OnAdd);
    }
}
