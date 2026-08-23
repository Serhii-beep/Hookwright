using Hookwright.Core.Events;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hookwright.EntityFrameworkCore.Configurations;

internal sealed class EventTypeConfiguration : IEntityTypeConfiguration<EventType>
{
    public void Configure(EntityTypeBuilder<EventType> builder)
    {
        builder.ToTable(HookwrightTables.EventTypes);

        builder.HasKey(t => t.Name);

        builder.Property(t => t.Name).HasMaxLength(EventType.MaxNameLength);

        builder.Property(t => t.CreatedAt);

        builder.Property(t => t.Description).HasMaxLength(EventType.MaxDescriptionLength);
    }
}