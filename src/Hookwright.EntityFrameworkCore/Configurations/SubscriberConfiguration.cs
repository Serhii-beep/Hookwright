using Hookwright.Core.Subscribers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hookwright.EntityFrameworkCore.Configurations;

internal sealed class SubscriberConfiguration : IEntityTypeConfiguration<Subscriber>
{
    public void Configure(EntityTypeBuilder<Subscriber> builder)
    {
        builder.ToTable(HookwrightTables.Subscribers);

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ExternalId)
            .HasMaxLength(Subscriber.MaxExternalIdLength)
            .IsRequired();

        builder.Property(s => s.Name)
            .HasMaxLength(Subscriber.MaxNameLength);

        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.DisabledAt);

        builder.Ignore(s => s.IsEnabled);

        builder.HasIndex(s => s.ExternalId)
            .IsUnique()
            .HasDatabaseName($"ux_{HookwrightTables.Subscribers}_external_id");
    }
}