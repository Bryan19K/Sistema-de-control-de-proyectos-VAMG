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

public sealed class ProgressSnapshotConfiguration : IEntityTypeConfiguration<ProgressSnapshot>
{
    public void Configure(EntityTypeBuilder<ProgressSnapshot> b)
    {
        b.Property(x => x.ProgressPercent).HasPrecision(7, 2);
        b.Property(x => x.ElapsedPercent).HasPrecision(7, 2);
        b.HasIndex(x => new { x.ProjectId, x.Date });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
    }
}

public sealed class ProjectPhaseConfiguration : IEntityTypeConfiguration<ProjectPhase>
{
    public void Configure(EntityTypeBuilder<ProjectPhase> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.ParentName).HasMaxLength(120);
        b.Property(x => x.Weight).HasPrecision(7, 2);
        b.Property(x => x.Percent).HasPrecision(7, 2);
        b.HasIndex(x => x.ProjectId);
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
    }
}

public sealed class BuildingRecordConfiguration : IEntityTypeConfiguration<BuildingRecord>
{
    public void Configure(EntityTypeBuilder<BuildingRecord> b)
    {
        b.Property(x => x.Level).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.ProgressPercent).HasPrecision(7, 2);
        b.Property(x => x.Status).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.ProjectId, x.Kind });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
    }
}

public sealed class ExpenseFundConfiguration : IEntityTypeConfiguration<ExpenseFund>
{
    public void Configure(EntityTypeBuilder<ExpenseFund> b)
    {
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.SourceKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.SourceKey);
    }
}

public sealed class ExpenseEntryConfiguration : IEntityTypeConfiguration<ExpenseEntry>
{
    public void Configure(EntityTypeBuilder<ExpenseEntry> b)
    {
        b.Property(x => x.Description).HasMaxLength(400).IsRequired();
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.Supplier).HasMaxLength(160);
        b.Property(x => x.InvoiceNumber).HasMaxLength(60);
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.Status).HasMaxLength(40);
        b.Property(x => x.PaidBy).HasMaxLength(100);
        b.Property(x => x.CostCenter).HasMaxLength(40);
        b.Property(x => x.SourceKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.Subtotal).HasPrecision(18, 4);
        b.Property(x => x.Vat).HasPrecision(18, 4);
        b.Property(x => x.Total).HasPrecision(18, 4);
        b.HasIndex(x => x.SourceKey);
        b.HasIndex(x => x.Date);
        b.HasOne(x => x.Fund).WithMany().HasForeignKey(x => x.FundId);
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
    }
}

public sealed class PriceItemConfiguration : IEntityTypeConfiguration<PriceItem>
{
    public void Configure(EntityTypeBuilder<PriceItem> b)
    {
        b.Property(x => x.Section).HasMaxLength(30).IsRequired();
        b.Property(x => x.Item).HasMaxLength(160).IsRequired();
        b.Property(x => x.Type).HasMaxLength(40);
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.TargetPrice).HasPrecision(18, 4);
        b.HasIndex(x => new { x.ProjectId, x.Section });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
    }
}

public sealed class ContractOrderConfiguration : IEntityTypeConfiguration<ContractOrder>
{
    public void Configure(EntityTypeBuilder<ContractOrder> b)
    {
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.OrderNumber).HasMaxLength(40);
        b.Property(x => x.ContractorName).HasMaxLength(120);
        b.Property(x => x.MaterialStatus).HasMaxLength(40);
        b.Property(x => x.ConstructionStatus).HasMaxLength(40);
        b.Property(x => x.LiquidationStatus).HasMaxLength(40);
        b.Property(x => x.Subtotal).HasPrecision(18, 4);
        b.Property(x => x.Vat).HasPrecision(18, 4);
        b.Property(x => x.Total).HasPrecision(18, 4);
        b.HasIndex(x => new { x.ProjectId, x.Code });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
        b.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
    }
}

public sealed class ContractorPersonConfiguration : IEntityTypeConfiguration<ContractorPerson>
{
    public void Configure(EntityTypeBuilder<ContractorPerson> b)
    {
        b.Property(x => x.FullName).HasMaxLength(160).IsRequired();
        b.Property(x => x.Dui).HasMaxLength(40);
        b.Property(x => x.Position).HasMaxLength(80);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.InsuranceType).HasMaxLength(80);
        b.Property(x => x.InsuranceStatus).HasMaxLength(200);
        b.Property(x => x.Insurer).HasMaxLength(120);
        b.Property(x => x.PolicyNumber).HasMaxLength(60);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.ContractorId);
        b.HasOne(x => x.Contractor).WithMany(c => c.People).HasForeignKey(x => x.ContractorId);
    }
}
