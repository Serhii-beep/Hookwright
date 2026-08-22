using Hookwright.Core.Endpoints;
using Hookwright.Core.Subscribers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hookwright.EntityFrameworkCore.Configurations;

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable(HookwrightTables.Endpoints);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.SubscriberId);
        builder.Property(e => e.CreatedAt);

        builder.Property(e => e.Url)
            .HasMaxLength(WebhookEndpoint.MaxUrlLength)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(WebhookEndpoint.MaxDescriptionLength);

        builder.Property<List<string>>("_eventTypeFilter");

        builder.Ignore(e => e.EventTypeFilter);
        builder.Ignore(e => e.IsEnabled);
        builder.Ignore(e => e.IsVerified);

        builder.HasIndex(e => new { e.SubscriberId, e.Health })
            .HasDatabaseName($"ix_{HookwrightTables.Endpoints}_subscriber_id_health");

        builder.HasOne<Subscriber>()
            .WithMany()
            .HasForeignKey(e => e.SubscriberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}