using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore;

/// <summary>
/// Hookwright's tables in a context of their own.
/// </summary>
public class HookwrightDbContext : DbContext
{
    /// <summary>
    /// Creates a context
    /// </summary>
    public HookwrightDbContext(DbContextOptions<HookwrightDbContext> options)
        : base(options)
    {

    }

    /// <summary>
    /// Creates a context for a derived type.
    /// </summary>
    protected HookwrightDbContext(DbContextOptions options)
        : base(options)
    {

    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HookwrightDbContext).Assembly);
    }
}