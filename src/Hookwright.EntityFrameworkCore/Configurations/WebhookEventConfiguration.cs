using Hookwright.Core.Events;
using Hookwright.Core.Subscribers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hookwright.EntityFrameworkCore.Configurations;

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable(HookwrightTables.Events);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.SubscriberId);
        builder.Property(e => e.CreatedAt);

        builder.Property(e => e.Type)
            .HasMaxLength(EventType.MaxNameLength)
            .IsRequired();

        builder.Property(e => e.Payload)
            .IsRequired();

        builder.Property(e => e.PartitionKey)
            .HasMaxLength(WebhookEvent.MaxPartitionKeyLength);

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(WebhookEvent.MaxIdempotencyKeyLength);

        builder.Property<Dictionary<string, string>>("_headers")
            .HasConversion(new HeaderDictionaryConverter(), new HeaderDictionaryComparer())
            .IsRequired();

        builder.Ignore(e => e.Headers);

        builder.HasIndex(e => new { e.SubscriberId, e.Id })
            .HasDatabaseName($"ix_{HookwrightTables.Events}_subscriber_id_id");

        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName($"ix_{HookwrightTables.Events}_created_at");

        builder.HasIndex(e => e.IdempotencyKey)
            .HasDatabaseName($"ix_{HookwrightTables.Events}_idempotency_key");

        builder.HasOne<Subscriber>()
            .WithMany()
            .HasForeignKey(e => e.SubscriberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}