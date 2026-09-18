using Amazon.AuroraDsql.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests;

[Collection(DsqlCollection.Name)]
public class DsqlIntegrationTests
{
    private readonly DsqlFixture _fixture;

    public DsqlIntegrationTests(DsqlFixture fixture)
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
    public async Task Navigates_from_widget_to_owner()
    {
        var ownerId = Guid.NewGuid();
        var widgetId = Guid.NewGuid();

        await using (var context = _fixture.CreateContext())
        {
            context.Owners.Add(new Owner { Id = ownerId, Name = "navigation" });
            context.Widgets.Add(new Widget
            {
                Id = widgetId,
                Name = "nav-widget",
                Quantity = 1,
                OwnerId = ownerId,
            });
            await context.SaveChangesAsync();
        }

        await using (var context = _fixture.CreateContext())
        {
            var widget = await context.Widgets
                .Include(w => w.Owner)
                .SingleAsync(w => w.Id == widgetId);

            Assert.NotNull(widget.Owner);
            Assert.Equal("navigation", widget.Owner!.Name);
        }
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

    [Fact]
    public async Task SaveChanges_over_the_row_limit_reports_54000()
    {
        await using var context = _fixture.CreateContext();

        // DSQL caps a transaction at 3,000 mutated rows; the statement that crosses the limit fails
        // with SQLSTATE 54000 and aborts the transaction. EF batches the inserts but keeps them in
        // one transaction, so the cap is hit inside SaveChanges and must surface unchanged.
        for (var i = 0; i < 3_001; i++)
        {
            context.Owners.Add(new Owner { Id = Guid.NewGuid(), Name = $"over-limit-{i}" });
        }

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("54000", postgresException.SqlState);

        // The aborted transaction leaves nothing behind.
        await using var verify = _fixture.CreateContext();
        Assert.False(await verify.Owners.AnyAsync(o => o.Name.StartsWith("over-limit-")));
    }

    [Fact]
    public async Task Row_limit_is_respected_when_chunking_across_transactions()
    {
        const int total = 3_001;
        const int chunkSize = 1_000;

        for (var offset = 0; offset < total; offset += chunkSize)
        {
            await using var context = _fixture.CreateContext();
            for (var i = offset; i < Math.Min(offset + chunkSize, total); i++)
            {
                context.Owners.Add(new Owner { Id = Guid.NewGuid(), Name = $"chunked-{i}" });
            }

            await context.SaveChangesAsync();
        }

        await using var verify = _fixture.CreateContext();
        Assert.Equal(total, await verify.Owners.CountAsync(o => o.Name.StartsWith("chunked-")));
    }

    [Fact]
    public async Task Generated_migration_ddl_is_idempotent()
    {
        await using var context = _fixture.CreateContext();
        var generator = context.GetService<IMigrationsSqlGenerator>();

        MigrationOperation[] Operations() =>
        [
            new CreateTableOperation
            {
                Name = "Idempotent",
                Columns =
                {
                    new AddColumnOperation
                    {
                        Name = "Id",
                        Table = "Idempotent",
                        ClrType = typeof(Guid),
                        ColumnType = "uuid",
                        IsNullable = false,
                    },
                },
                PrimaryKey = new AddPrimaryKeyOperation
                {
                    Name = "PK_Idempotent",
                    Table = "Idempotent",
                    Columns = ["Id"],
                },
            },
            new CreateIndexOperation
            {
                Name = "IX_Idempotent_Id",
                Table = "Idempotent",
                Columns = ["Id"],
            },
            new AddColumnOperation
            {
                Name = "Name",
                Table = "Idempotent",
                ClrType = typeof(string),
                ColumnType = "text",
                IsNullable = true,
            },
        ];

        var commands = generator.Generate(Operations(), context.Model)
            .Select(c => c.CommandText)
            .ToList();

        await using var connection = await _fixture.DataSource.OpenConnectionAsync();

        // Start from a clean slate, then apply the same DDL twice: the second run must not fail on
        // already-created objects.
        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = "DROP TABLE IF EXISTS \"Idempotent\"";
            await drop.ExecuteNonQueryAsync();
        }

        for (var run = 0; run < 2; run++)
        {
            foreach (var sql in commands)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
        }

        await using var cleanup = connection.CreateCommand();
        cleanup.CommandText = "DROP TABLE IF EXISTS \"Idempotent\"";
        await cleanup.ExecuteNonQueryAsync();
    }
}
