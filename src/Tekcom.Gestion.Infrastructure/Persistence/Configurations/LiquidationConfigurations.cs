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
