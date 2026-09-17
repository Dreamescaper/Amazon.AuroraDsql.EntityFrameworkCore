using System.Globalization;
using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Microsoft.Extensions.Configuration;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public static class TestEnvironment
{
    public static IConfiguration Config { get; }

    static TestEnvironment()
    {
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.json", optional: true)
            .AddJsonFile("config.test.json", optional: true)
            .AddEnvironmentVariables();

        Config = configBuilder.Build()
            .GetSection("Test:Npgsql");

        Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
    }

    private const string DefaultConnectionString = "Server=localhost;Username=admin;Password=token;Port=5432;SSL Mode=Require";

    public static string DefaultConnection
        => Config["DefaultConnection"] ?? Environment.GetEnvironmentVariable("DSQL_TEST_CONNECTION") ?? DefaultConnectionString;

    private static NpgsqlDataSource? _dataSource;

    public static NpgsqlDataSource DataSource
        => _dataSource ??= new NpgsqlDataSourceBuilder(DefaultConnection).Build();

    private static Version? _postgresVersion;

    public static Version PostgresVersion
    {
        get
        {
            if (_postgresVersion is not null)
            {
                return _postgresVersion;
            }

            using var conn = new NpgsqlConnection(NpgsqlTestStore.CreateConnectionString("postgres"));
            conn.Open();
            return _postgresVersion = conn.PostgreSqlVersion;
        }
    }

    private static bool? _isPostgisAvailable;

    public static bool IsPostgisAvailable
    {
        get
        {
            if (_isPostgisAvailable.HasValue)
            {
                return _isPostgisAvailable.Value;
            }

            using var conn = new NpgsqlConnection(NpgsqlTestStore.CreateConnectionString("postgres"));
            conn.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_available_extensions WHERE \"name\" = 'postgis' LIMIT 1)";
            _isPostgisAvailable = (bool)cmd.ExecuteScalar()!;
            return _isPostgisAvailable.Value;
        }
    }
}
