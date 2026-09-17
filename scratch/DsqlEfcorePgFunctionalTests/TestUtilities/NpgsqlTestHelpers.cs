using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Diagnostics.Internal;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class NpgsqlTestHelpers : RelationalTestHelpers
{
    protected NpgsqlTestHelpers() { }

    public static NpgsqlTestHelpers Instance { get; } = new();

    public override IServiceCollection AddProviderServices(IServiceCollection services)
        => services.AddEntityFrameworkNpgsql().AddEntityFrameworkDsql();

    public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseDsql(TestEnvironment.DataSource);

    public override LoggingDefinitions LoggingDefinitions { get; } = new NpgsqlLoggingDefinitions();
}
