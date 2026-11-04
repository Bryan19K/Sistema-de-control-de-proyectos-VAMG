using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tekcom.Gestion.Domain.Entities;

namespace Tekcom.Gestion.Infrastructure.Persistence.Configurations;

public sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> b) => b.HasIndex(x => x.Type);
}

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> b)
    {
        b.Property(x => x.UserId).HasMaxLength(450);
        b.Property(x => x.Recipient).HasMaxLength(256).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Error).HasMaxLength(500);
        b.Property(x => x.DedupeKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.DedupeKey);
        b.HasIndex(x => x.CreatedAt);
    }
}
