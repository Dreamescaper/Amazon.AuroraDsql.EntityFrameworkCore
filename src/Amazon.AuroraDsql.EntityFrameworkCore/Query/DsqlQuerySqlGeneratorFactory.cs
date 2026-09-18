using Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Query;

/// <summary>
/// Builds a query SQL generator that can emit <c>NULLS FIRST</c> to match the ordering EF and SQL
/// Server use by default (PostgreSQL sorts nulls last). Opt in with
/// <c>UseDsql(..., dsql =&gt; dsql.NullsFirst())</c>.
/// </summary>
internal sealed class DsqlQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly INpgsqlSingletonOptions _npgsqlSingletonOptions;
    private readonly DsqlOptionsExtension _options;

    public DsqlQuerySqlGeneratorFactory(
        QuerySqlGeneratorDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource,
        INpgsqlSingletonOptions npgsqlSingletonOptions,
        IDbContextOptions contextOptions)
    {
        _dependencies = dependencies;
        _typeMappingSource = typeMappingSource;
        _npgsqlSingletonOptions = npgsqlSingletonOptions;
        _options = contextOptions.FindExtension<DsqlOptionsExtension>() ?? new DsqlOptionsExtension();
    }

    public QuerySqlGenerator Create()
        => new NpgsqlQuerySqlGenerator(
            _dependencies,
            _typeMappingSource,
            _options.NullsFirst || _npgsqlSingletonOptions.ReverseNullOrderingEnabled,
            _npgsqlSingletonOptions.PostgresVersion);
}
