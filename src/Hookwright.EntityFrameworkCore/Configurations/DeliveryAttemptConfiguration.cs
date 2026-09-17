using Hookwright.Core.Deliveries;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hookwright.EntityFrameworkCore.Configurations;

internal sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable(HookwrightTables.DeliveryAttempts);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.DeliveryId);
        builder.Property(a => a.Outcome);
        builder.Property(a => a.AttemptedAt);
        builder.Property(a => a.ResponseStatusCode);

        builder.Property(a => a.Duration)
            .HasColumnName("duration_ms");

        builder.Property(a => a.ResponseBodySnippet)
            .HasMaxLength(DeliveryAttempt.MaxResponseBodySnippetLength);

        builder.Property(a => a.ErrorDetail)
            .HasMaxLength(DeliveryAttempt.MaxErrorDetailLength);

        builder.Property(a => a.WorkerId)
            .HasMaxLength(DeliveryAttempt.MaxWorkerIdLength)
            .IsRequired();

        builder.Property<Dictionary<string, string>>("_responseHeaders")
            .HasConversion(new HeaderDictionaryConverter(), new HeaderDictionaryComparer())
            .IsRequired();

        builder.Ignore(a => a.ResponseHeaders);

        builder.HasIndex(a => new { a.DeliveryId, a.AttemptedAt })
            .IsDescending(false, true)
            .HasDatabaseName($"ix_{HookwrightTables.DeliveryAttempts}_delivery_id_attempted_at");

        builder.HasIndex(a => a.AttemptedAt)
            .HasDatabaseName($"ix_{HookwrightTables.DeliveryAttempts}_attempted_at");

        builder.HasOne<Delivery>()
            .WithMany()
            .HasForeignKey(a => a.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}