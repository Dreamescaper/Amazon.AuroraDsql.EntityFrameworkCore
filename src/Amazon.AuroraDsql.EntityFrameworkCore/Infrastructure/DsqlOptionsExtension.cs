using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Options for the Aurora DSQL provider, layered on top of
/// <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>.
/// </summary>
public sealed class DsqlOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        // DSQL-specific service overrides are registered here as they are implemented.
        // This extension is applied after NpgsqlOptionsExtension, so Replace(...) calls win.
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

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using AuroraDsql ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
        }
    }
}
