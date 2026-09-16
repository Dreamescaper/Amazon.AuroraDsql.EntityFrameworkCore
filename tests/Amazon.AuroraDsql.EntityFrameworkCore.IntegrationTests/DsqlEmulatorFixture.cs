using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests;

public sealed class DsqlEmulatorFixture : IAsyncLifetime
{
    private const string Image = "ghcr.io/dreamescaper/dsql-emulator:0.1.1";

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(5432, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("proxy listening"))
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString =
            $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(5432)};"
            + "Username=admin;Password=an-iam-token;Database=postgres;"
            + "SSL Mode=Require;Pooling=false";

        DataSource = new NpgsqlDataSourceBuilder(ConnectionString).Build();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    public DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>().UseDsql(DataSource).Options;

    public EmulatorContext CreateContext() => new(Options<EmulatorContext>());
}

[CollectionDefinition(Name)]
public sealed class DsqlEmulatorCollection : ICollectionFixture<DsqlEmulatorFixture>
{
    public const string Name = "dsql-emulator";
}
