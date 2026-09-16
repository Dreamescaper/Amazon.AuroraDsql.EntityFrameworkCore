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
