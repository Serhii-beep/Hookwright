using Hookwright.Core.Endpoints;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hookwright.EntityFrameworkCore.Configurations;

internal sealed class EndpointSecretConfiguration : IEntityTypeConfiguration<EndpointSecret>
{
    public void Configure(EntityTypeBuilder<EndpointSecret> builder)
    {
        builder.ToTable(HookwrightTables.EndpointSecrets);

        builder.HasKey(s => s.Id);

        builder.Property(s => s.EndpointId);
        builder.Property(s => s.Algorithm);
        builder.Property(s => s.ValidFrom);

        builder.Property(s => s.ProtectedKey)
            .HasMaxLength(EndpointSecret.MaxProtectedKeyLength)
            .IsRequired();

        builder.Ignore(s => s.IsRetired);

        builder.HasIndex(s => new { s.EndpointId, s.ValidUntil })
            .HasDatabaseName($"ix_{HookwrightTables.EndpointSecrets}_endpoint_id_valid_until");

        builder.HasOne<WebhookEndpoint>()
            .WithMany()
            .HasForeignKey(s => s.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}