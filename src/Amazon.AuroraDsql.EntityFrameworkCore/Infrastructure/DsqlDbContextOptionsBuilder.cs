using Microsoft.EntityFrameworkCore;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Allows configuration of DSQL-specific options for a <see cref="DbContext" />.
/// </summary>
public class DsqlDbContextOptionsBuilder
{
    public DsqlDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        => OptionsBuilder = optionsBuilder ?? throw new ArgumentNullException(nameof(optionsBuilder));

    protected DbContextOptionsBuilder OptionsBuilder { get; }
}
