using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tekcom.Gestion.Domain.Entities;

namespace Tekcom.Gestion.Infrastructure.Persistence.Configurations;

public sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Brand).HasMaxLength(80);
        b.Property(x => x.Model).HasMaxLength(80);
        b.Property(x => x.Serial).HasMaxLength(80);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.ProjectId, x.Serial });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
        b.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
    }
}

public sealed class EquipmentSlotConfiguration : IEntityTypeConfiguration<EquipmentSlot>
{
    public void Configure(EntityTypeBuilder<EquipmentSlot> b)
    {
        b.Property(x => x.CardModel).HasMaxLength(80);
        b.Property(x => x.CardSerial).HasMaxLength(80);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.EquipmentId, x.SlotNumber });
    }
}

public sealed class TransceiverConfiguration : IEntityTypeConfiguration<Transceiver>
{
    public void Configure(EntityTypeBuilder<Transceiver> b)
    {
        b.Property(x => x.Model).HasMaxLength(80);
        b.Property(x => x.Serial).HasMaxLength(80).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.ProjectId, x.Serial });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
        b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId);
        b.HasOne(x => x.Slot).WithMany().HasForeignKey(x => x.SlotId);
    }
}

public sealed class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.StoredName).HasMaxLength(260).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        b.HasIndex(x => new { x.ProjectId, x.SiteId });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
        b.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
        b.HasOne(x => x.TaskItem).WithMany().HasForeignKey(x => x.TaskItemId);
    }
}

public sealed class MinuteConfiguration : IEntityTypeConfiguration<Minute>
{
    public void Configure(EntityTypeBuilder<Minute> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Attendees).HasMaxLength(500);
        b.Property(x => x.Summary).HasMaxLength(4000);
        b.HasIndex(x => new { x.ProjectId, x.Date });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
    }
}

public sealed class MinuteItemConfiguration : IEntityTypeConfiguration<MinuteItem>
{
    public void Configure(EntityTypeBuilder<MinuteItem> b)
    {
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ResponsibleUserId).HasMaxLength(450);
        b.HasOne(x => x.Minute).WithMany(m => m.Items).HasForeignKey(x => x.MinuteId);
        b.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
    }
}
