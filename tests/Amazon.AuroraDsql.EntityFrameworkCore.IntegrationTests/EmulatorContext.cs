using Microsoft.EntityFrameworkCore;

namespace Amazon.AuroraDsql.EntityFrameworkCore.IntegrationTests;

public class Widget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public List<int> Numbers { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public Guid? OwnerId { get; set; }
    public Owner? Owner { get; set; }
}

public class Owner
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class EmulatorContext : DbContext
{
    public EmulatorContext(DbContextOptions<EmulatorContext> options)
        : base(options)
    {
    }

    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<Owner> Owners => Set<Owner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Widget>()
            .HasOne(w => w.Owner)
            .WithMany()
            .HasForeignKey(w => w.OwnerId);
}
