using System.Globalization;
using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Amazon.AuroraDsql.Npgsql;
using Microsoft.Extensions.Configuration;
using DsqlConnector = Amazon.AuroraDsql.Npgsql.AuroraDsql;

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

    private const string EmulatorConnectionString = "Server=localhost;Username=admin;Password=token;Port=5432;SSL Mode=Require";

    /// <summary>The live cluster endpoint, if one was configured (otherwise the emulator is used).</summary>
    public static string? ClusterEndpoint
        => Environment.GetEnvironmentVariable("DSQL_CLUSTER_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("CLUSTER_ENDPOINT");

    public static bool IsLive => !string.IsNullOrWhiteSpace(ClusterEndpoint);

    public static string DefaultConnection
        => Config["DefaultConnection"]
            ?? Environment.GetEnvironmentVariable("DSQL_TEST_CONNECTION")
            ?? EmulatorConnectionString;

    private static DsqlDataSource? _liveDataSource;
    private static NpgsqlDataSource? _dataSource;

    /// <summary>
    /// Data source for the configured target: the AWS connector (IAM auth) for a live cluster, or a
    /// plain Npgsql data source for the emulator.
    /// </summary>
    public static NpgsqlDataSource DataSource
    {
        get
        {
            if (_dataSource is not null)
            {
                return _dataSource;
            }

            if (IsLive)
            {
                Console.WriteLine($"Tests target: live cluster {ClusterEndpoint}");
                _liveDataSource = DsqlConnector.CreateDataSourceAsync(new DsqlConfig { Host = ClusterEndpoint! })
                    .GetAwaiter().GetResult();
                return _dataSource = _liveDataSource.DataSource;
            }

            Console.WriteLine("Tests target: dsql-emulator");
            return _dataSource = new NpgsqlDataSourceBuilder(DefaultConnection).Build();
        }
    }

    private static Version? _postgresVersion;

    public static Version PostgresVersion
    {
        get
        {
            if (_postgresVersion is not null)
            {
                return _postgresVersion;
            }

            using var conn = DataSource.OpenConnection();
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

            using var conn = DataSource.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_available_extensions WHERE \"name\" = 'postgis' LIMIT 1)";
            _isPostgisAvailable = (bool)cmd.ExecuteScalar()!;
            return _isPostgisAvailable.Value;
        }
    }
}
