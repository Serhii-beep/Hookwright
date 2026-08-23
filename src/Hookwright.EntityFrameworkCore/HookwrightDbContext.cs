using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Subscribers;

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
        modelBuilder.UseSnakeCaseColumnNames();
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<SubscriberId>().HaveConversion<PrefixedIdConverter<SubscriberId>>();
        configurationBuilder.Properties<WebhookEndpointId>().HaveConversion<PrefixedIdConverter<WebhookEndpointId>>();
        configurationBuilder.Properties<EndpointSecretId>().HaveConversion<PrefixedIdConverter<EndpointSecretId>>();
        configurationBuilder.Properties<WebhookEventId>().HaveConversion<PrefixedIdConverter<WebhookEventId>>();
        configurationBuilder.Properties<DeliveryId>().HaveConversion<PrefixedIdConverter<DeliveryId>>();
        configurationBuilder.Properties<DeliveryAttemptId>().HaveConversion<PrefixedIdConverter<DeliveryAttemptId>>();
        configurationBuilder.Properties<TimeSpan>().HaveConversion<DurationConverter>();

        ProviderConventions.Apply(configurationBuilder, Database.ProviderName);
    }
}