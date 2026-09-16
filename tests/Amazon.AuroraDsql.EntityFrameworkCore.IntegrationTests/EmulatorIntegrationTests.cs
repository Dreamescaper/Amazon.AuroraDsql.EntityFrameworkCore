using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests;

[Collection(DsqlEmulatorCollection.Name)]
public class EmulatorIntegrationTests
{
    private readonly DsqlEmulatorFixture _fixture;

    public EmulatorIntegrationTests(DsqlEmulatorFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task Can_connect_and_query()
    {
        await using var context = _fixture.CreateContext();

        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Migration_is_recorded()
    {
        await using var context = _fixture.CreateContext();

        var applied = await context.Database.GetAppliedMigrationsAsync();

        Assert.Contains("20260101000000_Initial", applied);
    }

    [Fact]
    public async Task Round_trips_jsonb_collections()
    {
        var id = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            context.Widgets.Add(new Widget
            {
                Id = id,
                Name = "round-trip",
                Quantity = 7,
                Numbers = [1, 2, 3],
                Tags = ["a", "b"],
            });
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var widget = await context.Widgets.SingleAsync(w => w.Id == id);

            Assert.Equal([1, 2, 3], widget.Numbers);
            Assert.Equal(["a", "b"], widget.Tags);
        }
    }

    [Fact]
    public async Task Queries_collection_containment()
    {
        var id = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            context.Widgets.Add(new Widget
            {
                Id = id,
                Name = "containment",
                Quantity = 1,
                Numbers = [10, 20, 30],
            });
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var found = await context.Widgets.AnyAsync(w => w.Id == id && w.Numbers.Contains(20));

            Assert.True(found);
        }
    }

    [Fact]
    public async Task SaveChanges_inside_explicit_transaction_does_not_use_savepoints()
    {
        await using var context = _fixture.CreateContext();
        var strategy = context.Database.CreateExecutionStrategy();

        // EF requires user-initiated transactions to run inside the execution strategy.
        // If a SAVEPOINT were emitted, the emulator would reject it.
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Widgets.Add(new Widget
            {
                Id = Guid.NewGuid(),
                Name = "in-transaction",
                Quantity = 2,
            });
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
        });
    }

    [Fact]
    public async Task Foreign_key_accepts_existing_principal()
    {
        var ownerId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();

        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO \"Owners\" (\"Id\", \"Name\") VALUES ({ownerId}, 'owner')");

        context.Widgets.Add(new Widget
        {
            Id = Guid.NewGuid(),
            Name = "with-owner",
            Quantity = 1,
            OwnerId = ownerId,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Foreign_key_rejects_missing_principal()
    {
        await using var context = _fixture.CreateContext();

        context.Widgets.Add(new Widget
        {
            Id = Guid.NewGuid(),
            Name = "bad-owner",
            Quantity = 1,
            OwnerId = Guid.NewGuid(),
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23503", postgresException.SqlState);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_commits()
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();

        await context.ExecuteInTransactionAsync(async ct =>
        {
            context.Widgets.Add(new Widget { Id = id, Name = "helper", Quantity = 3 });
            await context.SaveChangesAsync(ct);
        });

        await using var verify = _fixture.CreateContext();
        Assert.True(await verify.Widgets.AnyAsync(w => w.Id == id));
    }
}
