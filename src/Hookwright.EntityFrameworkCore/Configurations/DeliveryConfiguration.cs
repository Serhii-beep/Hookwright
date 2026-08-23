using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hookwright.EntityFrameworkCore.Configurations;

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable(HookwrightTables.Deliveries);

        builder.HasKey(d => d.Id);

        builder.Property(d => d.EventId);
        builder.Property(d => d.EndpointId);
        builder.Property(d => d.CreatedAt);

        builder.Property(d => d.PartitionKey)
            .HasMaxLength(WebhookEvent.MaxPartitionKeyLength);

        builder.Property(d => d.LeaseOwner)
            .HasMaxLength(Delivery.MaxLeaseOwnerLength);

        builder.Ignore(d => d.IsTerminal);

        builder.HasIndex(d => new { d.State, d.NextAttemptAt })
            .HasDatabaseName($"ix_{HookwrightTables.Deliveries}_state_next_attempt_at");

        builder.HasIndex(d => new { d.EndpointId, d.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName($"ix_{HookwrightTables.Deliveries}_endpoint_id_created_at");

        builder.HasIndex(d => d.EventId)
            .HasDatabaseName($"ix_{HookwrightTables.Deliveries}_event_id");

        builder.HasOne<WebhookEvent>()
            .WithMany()
            .HasForeignKey(d => d.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WebhookEndpoint>()
            .WithMany()
            .HasForeignKey(d => d.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}