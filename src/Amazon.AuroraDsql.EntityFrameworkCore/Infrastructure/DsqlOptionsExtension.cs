using Amazon.AuroraDsql.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Options for the Aurora DSQL provider, layered on top of
/// <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>.
/// </summary>
public sealed class DsqlOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DsqlOptionsExtension()
    {
    }

    private DsqlOptionsExtension(DsqlOptionsExtension copyFrom)
    {
        UseIdentityColumns = copyFrom.UseIdentityColumns;
        IdentityCacheSize = copyFrom.IdentityCacheSize;
    }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    internal bool UseIdentityColumns { get; private init; }

    internal int IdentityCacheSize { get; private init; } = DsqlDbContextOptionsBuilder.DefaultIdentityCacheSize;

    public DsqlOptionsExtension WithIdentityColumns(int cacheSize)
        => new(this) { UseIdentityColumns = true, IdentityCacheSize = cacheSize };

    public void ApplyServices(IServiceCollection services)
    {
        // This extension is applied after NpgsqlOptionsExtension, so Replace(...) calls win.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IConventionSetPlugin, DsqlConventionSetPlugin>());
        services.Replace(ServiceDescriptor.Scoped<IModelValidator, DsqlModelValidator>());
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(DsqlOptionsExtension extension)
            : base(extension)
        {
        }

        private new DsqlOptionsExtension Extension => (DsqlOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment
            => $"using AuroraDsql(identityColumns: {Extension.UseIdentityColumns}) ";

        public override int GetServiceProviderHashCode()
            => HashCode.Combine(Extension.UseIdentityColumns, Extension.IdentityCacheSize);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo
                && Extension.UseIdentityColumns == otherInfo.Extension.UseIdentityColumns
                && Extension.IdentityCacheSize == otherInfo.Extension.IdentityCacheSize;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["AuroraDsql:UseIdentityColumns"] = Extension.UseIdentityColumns.ToString();
            debugInfo["AuroraDsql:IdentityCacheSize"] = Extension.IdentityCacheSize.ToString();
        }
    }
}
