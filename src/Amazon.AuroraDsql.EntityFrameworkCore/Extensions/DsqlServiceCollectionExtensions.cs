using Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;
using Amazon.AuroraDsql.EntityFrameworkCore.Metadata;
using Amazon.AuroraDsql.EntityFrameworkCore.Migrations;
using Amazon.AuroraDsql.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Extensions;

/// <summary>
/// Registers the Aurora DSQL service overrides directly on an <see cref="IServiceCollection" />.
/// </summary>
/// <remarks>
/// Normally these are applied by <see cref="Infrastructure.DsqlOptionsExtension.ApplyServices" />
/// when EF builds the internal service provider from options. Use this method when the provider is
/// built externally (e.g. <c>UseInternalServiceProvider</c>, as EF's specification-test fixtures do).
/// </remarks>
public static class DsqlServiceCollectionExtensions
{
    public static IServiceCollection AddEntityFrameworkDsql(this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Scoped<IConventionSetPlugin, DsqlConventionSetPlugin>());
        serviceCollection.Replace(ServiceDescriptor.Singleton<IModelValidator, DsqlModelValidator>());
        serviceCollection.Replace(ServiceDescriptor.Singleton<IRelationalTypeMappingSource, DsqlTypeMappingSource>());
        serviceCollection.Replace(ServiceDescriptor.Scoped<IMigrationsSqlGenerator, DsqlMigrationsSqlGenerator>());
        serviceCollection.Replace(ServiceDescriptor.Scoped<IHistoryRepository, DsqlHistoryRepository>());
        serviceCollection.Replace(
            ServiceDescriptor.Scoped<IMigrationCommandExecutor, DsqlMigrationCommandExecutor>());
        serviceCollection.Replace(ServiceDescriptor.Scoped<INpgsqlRelationalConnection, DsqlRelationalConnection>());
        serviceCollection.Replace(
            ServiceDescriptor.Singleton<IRelationalTransactionFactory, DsqlRelationalTransactionFactory>());

        return serviceCollection;
    }
}
