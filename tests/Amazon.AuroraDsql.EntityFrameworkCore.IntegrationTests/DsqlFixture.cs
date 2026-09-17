using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Amazon.AuroraDsql.Npgsql;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DsqlConnector = Amazon.AuroraDsql.Npgsql.AuroraDsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests;

/// <summary>
/// Runs the integration suite against the local <c>dsql-emulator</c> (default) or a real Aurora DSQL
/// cluster. A live cluster is selected by setting <c>DSQL_CLUSTER_ENDPOINT</c> (or
/// <c>CLUSTER_ENDPOINT</c>); AWS credentials come from the SDK default chain.
/// </summary>
public sealed class DsqlFixture : IAsyncLifetime
{
    private const string EmulatorImage = "ghcr.io/dreamescaper/dsql-emulator:0.1.1";

    private IContainer? _container;
    private DsqlDataSource? _liveDataSource;

    public bool IsLive { get; private set; }

    public string Target { get; private set; } = string.Empty;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("DSQL_CLUSTER_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("CLUSTER_ENDPOINT");
        var externalConnectionString = Environment.GetEnvironmentVariable("DSQL_TEST_CONNECTION");

        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            // Point at an already-running instance (e.g. the emulator container started by hand).
            Target = "external connection string";
            DataSource = new NpgsqlDataSourceBuilder(externalConnectionString).Build();
        }
        else if (!string.IsNullOrWhiteSpace(endpoint))
        {
            IsLive = true;
            Target = $"live cluster {endpoint}";
            _liveDataSource = await DsqlConnector.CreateDataSourceAsync(new DsqlConfig { Host = endpoint });
            DataSource = _liveDataSource.DataSource;
        }
        else
        {
            Target = "dsql-emulator";
            _container = new ContainerBuilder(EmulatorImage)
                .WithPortBinding(5432, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("proxy listening"))
                .Build();
            await _container.StartAsync();

            var connectionString =
                $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(5432)};"
                + "Username=admin;Password=an-iam-token;Database=postgres;"
                + "SSL Mode=Require;Pooling=false";
            DataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        }

        Console.WriteLine($"Integration tests target: {Target}");

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_liveDataSource is not null)
        {
            await _liveDataSource.DisposeAsync();
        }
        else
        {
            await DataSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>().UseDsql(DataSource).Options;

    public IntegrationContext CreateContext() => new(Options<IntegrationContext>());
}

[CollectionDefinition(Name)]
public sealed class DsqlCollection : ICollectionFixture<DsqlFixture>
{
    public const string Name = "dsql";
}
