using Amazon.AuroraDsql.EntityFrameworkCore.Infrastructure;
using Amazon.AuroraDsql.Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Amazon.AuroraDsql.EntityFrameworkCore.Extensions;

/// <summary>
/// Configures a <see cref="DbContext" /> to use Aurora DSQL.
/// </summary>
public static class DsqlDbContextOptionsExtensions
{
    /// <summary>
    /// Configures the context to connect to Aurora DSQL using the given Npgsql data source.
    /// Use this overload for tests, the local emulator, or user-managed authentication.
    /// </summary>
    public static DbContextOptionsBuilder UseDsql(
        this DbContextOptionsBuilder optionsBuilder,
        NpgsqlDataSource dataSource,
        Action<DsqlDbContextOptionsBuilder>? dsqlOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(dataSource);

        // The data source is passed through as-is so that NpgsqlConnection/NpgsqlCommand identity is preserved.
        optionsBuilder.UseNpgsql(dataSource);
        AddDsqlExtension(optionsBuilder, dsqlOptionsAction);

        return optionsBuilder;
    }

    /// <summary>
    /// Configures the context to connect to Aurora DSQL using the AWS connector's data source.
    /// </summary>
    public static DbContextOptionsBuilder UseDsql(
        this DbContextOptionsBuilder optionsBuilder,
        DsqlDataSource dsqlDataSource,
        Action<DsqlDbContextOptionsBuilder>? dsqlOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(dsqlDataSource);

        return UseDsql(optionsBuilder, dsqlDataSource.DataSource, dsqlOptionsAction);
    }

    /// <summary>
    /// Configures the context to connect to Aurora DSQL using a data source registered in the
    /// application's service provider (<see cref="DsqlDataSource" /> first, then
    /// <see cref="NpgsqlDataSource" />).
    /// </summary>
    public static DbContextOptionsBuilder UseDsql(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        Action<DsqlDbContextOptionsBuilder>? dsqlOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (serviceProvider.GetService<DsqlDataSource>() is { } dsqlDataSource)
        {
            return UseDsql(optionsBuilder, dsqlDataSource, dsqlOptionsAction);
        }

        if (serviceProvider.GetService<NpgsqlDataSource>() is { } dataSource)
        {
            return UseDsql(optionsBuilder, dataSource, dsqlOptionsAction);
        }

        throw new InvalidOperationException(
            $"No {nameof(DsqlDataSource)} or {nameof(NpgsqlDataSource)} is registered in the service provider. "
            + $"Register one, or pass a data source to UseDsql directly.");
    }

    /// <inheritdoc cref="UseDsql(DbContextOptionsBuilder, NpgsqlDataSource, Action{DsqlDbContextOptionsBuilder}?)" />
    public static DbContextOptionsBuilder<TContext> UseDsql<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        NpgsqlDataSource dataSource,
        Action<DsqlDbContextOptionsBuilder>? dsqlOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDsql((DbContextOptionsBuilder)optionsBuilder, dataSource, dsqlOptionsAction);

    /// <inheritdoc cref="UseDsql(DbContextOptionsBuilder, DsqlDataSource, Action{DsqlDbContextOptionsBuilder}?)" />
    public static DbContextOptionsBuilder<TContext> UseDsql<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DsqlDataSource dsqlDataSource,
        Action<DsqlDbContextOptionsBuilder>? dsqlOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDsql((DbContextOptionsBuilder)optionsBuilder, dsqlDataSource, dsqlOptionsAction);

    /// <inheritdoc cref="UseDsql(DbContextOptionsBuilder, IServiceProvider, Action{DsqlDbContextOptionsBuilder}?)" />
    public static DbContextOptionsBuilder<TContext> UseDsql<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IServiceProvider serviceProvider,
        Action<DsqlDbContextOptionsBuilder>? dsqlOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDsql((DbContextOptionsBuilder)optionsBuilder, serviceProvider, dsqlOptionsAction);

    private static void AddDsqlExtension(
        DbContextOptionsBuilder optionsBuilder,
        Action<DsqlDbContextOptionsBuilder>? dsqlOptionsAction)
    {
        var dsqlBuilder = new DsqlDbContextOptionsBuilder(optionsBuilder);
        dsqlOptionsAction?.Invoke(dsqlBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(dsqlBuilder.Options);
    }
}
