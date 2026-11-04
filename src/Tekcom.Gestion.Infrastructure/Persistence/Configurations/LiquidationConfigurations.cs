using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tekcom.Gestion.Domain.Entities;

namespace Tekcom.Gestion.Infrastructure.Persistence.Configurations;

public sealed class LiquidationConfiguration : IEntityTypeConfiguration<Liquidation>
{
    public void Configure(EntityTypeBuilder<Liquidation> b)
    {
        b.Property(x => x.Observations).HasMaxLength(2000);
        b.Property(x => x.ClosedBy).HasMaxLength(256);
        b.HasIndex(x => x.SiteId);
    }
}

public sealed class LiquidationJustificationConfiguration : IEntityTypeConfiguration<LiquidationJustification>
{
    public void Configure(EntityTypeBuilder<LiquidationJustification> b) =>
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
}

public sealed class FatMeasurementConfiguration : IEntityTypeConfiguration<FatMeasurement>
{
    public void Configure(EntityTypeBuilder<FatMeasurement> b)
    {
        b.Property(x => x.FatCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.SiteId, x.FatCode });
    }
}

public sealed class ExportTemplateConfiguration : IEntityTypeConfiguration<ExportTemplate>
{
    public void Configure(EntityTypeBuilder<ExportTemplate> b)
    {
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.ReportKey).HasMaxLength(40);
        b.Property(x => x.HeaderColor).HasMaxLength(9).IsRequired();
        b.Property(x => x.AccentColor).HasMaxLength(9).IsRequired();
        b.Property(x => x.FooterNote).HasMaxLength(300);
        b.Property(x => x.HiddenColumns).HasMaxLength(500);
        b.Ignore(x => x.HiddenSet);
    }
}

public sealed class WorkPlanPhaseConfiguration : IEntityTypeConfiguration<WorkPlanPhase>
{
    public void Configure(EntityTypeBuilder<WorkPlanPhase> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasIndex(x => x.ProjectId);
    }
}

public sealed class WorkPlanActivityConfiguration : IEntityTypeConfiguration<WorkPlanActivity>
{
    public void Configure(EntityTypeBuilder<WorkPlanActivity> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.ProjectId, x.PhaseId });
        b.HasMany(x => x.Predecessors).WithOne(d => d.Activity).HasForeignKey(d => d.ActivityId);
    }
}

public sealed class ActivityDependencyConfiguration : IEntityTypeConfiguration<ActivityDependency>
{
    public void Configure(EntityTypeBuilder<ActivityDependency> b)
    {
        b.HasOne(x => x.Predecessor).WithMany().HasForeignKey(x => x.PredecessorId);
        b.HasIndex(x => new { x.ActivityId, x.PredecessorId });
    }
}

public sealed class PlanTemplateConfiguration : IEntityTypeConfiguration<PlanTemplate>
{
    public void Configure(EntityTypeBuilder<PlanTemplate> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
    }
}
