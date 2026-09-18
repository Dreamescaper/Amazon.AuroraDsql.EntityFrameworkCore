using Microsoft.EntityFrameworkCore;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Allows configuration of DSQL-specific options for a <see cref="DbContext" />.
/// </summary>
public class DsqlDbContextOptionsBuilder
{
    /// <summary>
    /// Default identity sequence cache size. A large cache distributes ID generation across DSQL
    /// nodes, avoiding hot-key contention on the sequence.
    /// </summary>
    public const int DefaultIdentityCacheSize = 65536;

    public DsqlDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        OptionsBuilder = optionsBuilder ?? throw new ArgumentNullException(nameof(optionsBuilder));
        Options = optionsBuilder.Options.FindExtension<DsqlOptionsExtension>() ?? new DsqlOptionsExtension();
    }

    protected DbContextOptionsBuilder OptionsBuilder { get; }

    internal DsqlOptionsExtension Options { get; private set; }

    /// <summary>Default maximum number of OCC retry attempts.</summary>
    public const int DefaultMaxRetryCount = 6;

    /// <summary>Default maximum delay between OCC retries.</summary>
    public static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sets the maximum number of OCC retry attempts. Default is <see cref="DefaultMaxRetryCount" />.
    /// </summary>
    public DsqlDbContextOptionsBuilder SetMaxRetryCount(int maxRetryCount)
    {
        if (maxRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryCount));
        }

        Options = Options.WithRetry(maxRetryCount, Options.MaxRetryDelay);
        return this;
    }

    /// <summary>
    /// Sets the maximum delay between OCC retries. Default is <see cref="DefaultMaxRetryDelay" />.
    /// </summary>
    public DsqlDbContextOptionsBuilder SetMaxRetryDelay(TimeSpan maxRetryDelay)
    {
        if (maxRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryDelay));
        }

        Options = Options.WithRetry(Options.MaxRetryCount, maxRetryDelay);
        return this;
    }

    /// <summary>
    /// Emits <c>NULLS FIRST</c> for orderings so nulls sort first, as EF and SQL Server do by
    /// default (PostgreSQL sorts nulls last).
    /// </summary>
    public DsqlDbContextOptionsBuilder NullsFirst(bool nullsFirst = true)
    {
        Options = Options.WithNullsFirst(nullsFirst);
        return this;
    }

    /// <summary>
    /// Enables identity-column support for <see cref="long" /> primary keys. DSQL accepts a cache
    /// size of either <c>1</c> or at least <see cref="DefaultIdentityCacheSize" />.
    /// </summary>
    public DsqlDbContextOptionsBuilder EnableIdentityColumns(int cacheSize = DefaultIdentityCacheSize)
    {
        if (cacheSize != 1 && cacheSize < DefaultIdentityCacheSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cacheSize),
                cacheSize,
                $"DSQL requires an identity cache size of 1 or at least {DefaultIdentityCacheSize}.");
        }

        Options = Options.WithIdentityColumns(cacheSize);
        return this;
    }
}
