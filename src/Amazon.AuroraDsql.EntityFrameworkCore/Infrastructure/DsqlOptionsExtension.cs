using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
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

    public DsqlOptionsExtension()
    {
    }

    private DsqlOptionsExtension(DsqlOptionsExtension copyFrom)
    {
        UseIdentityColumns = copyFrom.UseIdentityColumns;
        IdentityCacheSize = copyFrom.IdentityCacheSize;
        MaxRetryCount = copyFrom.MaxRetryCount;
        MaxRetryDelay = copyFrom.MaxRetryDelay;
        NullsFirst = copyFrom.NullsFirst;
    }

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    internal bool UseIdentityColumns { get; private init; }

    internal int IdentityCacheSize { get; private init; } = DsqlDbContextOptionsBuilder.DefaultIdentityCacheSize;

    internal int MaxRetryCount { get; private init; } = DsqlDbContextOptionsBuilder.DefaultMaxRetryCount;

    internal TimeSpan MaxRetryDelay { get; private init; } = DsqlDbContextOptionsBuilder.DefaultMaxRetryDelay;

    public DsqlOptionsExtension WithIdentityColumns(int cacheSize)
        => new(this) { UseIdentityColumns = true, IdentityCacheSize = cacheSize };

    public DsqlOptionsExtension WithRetry(int maxRetryCount, TimeSpan maxRetryDelay)
        => new(this) { MaxRetryCount = maxRetryCount, MaxRetryDelay = maxRetryDelay };

    internal bool NullsFirst { get; private init; }

    public DsqlOptionsExtension WithNullsFirst(bool nullsFirst)
        => new(this) { NullsFirst = nullsFirst };

    public void ApplyServices(IServiceCollection services)
    {
        // This extension is applied after NpgsqlOptionsExtension, so Replace(...) calls win.
        // The same registrations are available via AddEntityFrameworkDsql for external providers.
        services.AddEntityFrameworkDsql();
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
            => HashCode.Combine(
                Extension.UseIdentityColumns,
                Extension.IdentityCacheSize,
                Extension.MaxRetryCount,
                Extension.MaxRetryDelay,
                Extension.NullsFirst);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo
                && Extension.UseIdentityColumns == otherInfo.Extension.UseIdentityColumns
                && Extension.IdentityCacheSize == otherInfo.Extension.IdentityCacheSize
                && Extension.MaxRetryCount == otherInfo.Extension.MaxRetryCount
                && Extension.MaxRetryDelay == otherInfo.Extension.MaxRetryDelay
                && Extension.NullsFirst == otherInfo.Extension.NullsFirst;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["AuroraDsql:UseIdentityColumns"] = Extension.UseIdentityColumns.ToString();
            debugInfo["AuroraDsql:IdentityCacheSize"] = Extension.IdentityCacheSize.ToString();
            debugInfo["AuroraDsql:MaxRetryCount"] = Extension.MaxRetryCount.ToString();
            debugInfo["AuroraDsql:MaxRetryDelay"] = Extension.MaxRetryDelay.ToString();
            debugInfo["AuroraDsql:NullsFirst"] = Extension.NullsFirst.ToString();
        }
    }
}
