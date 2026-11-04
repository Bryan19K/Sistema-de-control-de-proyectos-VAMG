using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tekcom.Gestion.Domain.Entities;

namespace Tekcom.Gestion.Infrastructure.Persistence.Configurations;

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b) => b.Property(x => x.Name).HasMaxLength(150).IsRequired();
}

public sealed class ProjectTypeConfiguration : IEntityTypeConfiguration<ProjectType>
{
    public void Configure(EntityTypeBuilder<ProjectType> b)
    {
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code);
    }
}

public sealed class SupervisorConfiguration : IEntityTypeConfiguration<Supervisor>
{
    public void Configure(EntityTypeBuilder<Supervisor> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(40);
    }
}

public sealed class ContractorConfiguration : IEntityTypeConfiguration<Contractor>
{
    public void Configure(EntityTypeBuilder<Contractor> b)
    {
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.ResponsibleName).HasMaxLength(150);
        b.Property(x => x.Phone).HasMaxLength(40);
    }
}

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.ResponsibleName).HasMaxLength(150);
        b.Property(x => x.ReportedProgress).HasPrecision(7, 2);
        b.Property(x => x.PendingWork).HasMaxLength(1000);
        b.HasIndex(x => x.Code);
    }
}

public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> b)
    {
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Zone).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Ignore(x => x.IsReadyToDeliver);
        b.Ignore(x => x.MapUrl);
        b.HasIndex(x => new { x.ProjectId, x.Code });
    }
}

public sealed class StageDefinitionConfiguration : IEntityTypeConfiguration<StageDefinition>
{
    public void Configure(EntityTypeBuilder<StageDefinition> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.ProjectTypeId, x.Code });
    }
}

public sealed class SiteStageConfiguration : IEntityTypeConfiguration<SiteStage>
{
    public void Configure(EntityTypeBuilder<SiteStage> b)
    {
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.SiteId, x.Code });
    }
}

public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(60);
        b.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Model).HasMaxLength(120);
        b.Property(x => x.Description).HasMaxLength(600);
        b.HasIndex(x => x.Name);
    }
}

public sealed class MaterialAliasConfiguration : IEntityTypeConfiguration<MaterialAlias>
{
    public void Configure(EntityTypeBuilder<MaterialAlias> b)
    {
        b.Property(x => x.Alias).HasMaxLength(200).IsRequired();
        b.Property(x => x.AliasKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.AliasKey);
    }
}

public sealed class DesignProjectionConfiguration : IEntityTypeConfiguration<DesignProjection>
{
    public void Configure(EntityTypeBuilder<DesignProjection> b)
    {
        b.Property(x => x.Source).HasMaxLength(260);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.SiteId, x.Version });
    }
}

public sealed class DesignProjectionLineConfiguration : IEntityTypeConfiguration<DesignProjectionLine>
{
    public void Configure(EntityTypeBuilder<DesignProjectionLine> b) =>
        b.HasIndex(x => new { x.DesignProjectionId, x.MaterialId });
}

public sealed class ProjectionAdjustmentConfiguration : IEntityTypeConfiguration<ProjectionAdjustment>
{
    public void Configure(EntityTypeBuilder<ProjectionAdjustment> b)
    {
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.SiteId, x.MaterialId });
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        b.Property(x => x.Reference).HasMaxLength(120);
        b.Property(x => x.Batch).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.NegativeStockJustification).HasMaxLength(500);
        b.Property(x => x.VoidReason).HasMaxLength(500);
        b.Property(x => x.VoidedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.ProjectId, x.MaterialId, x.Type });
        b.HasIndex(x => new { x.SiteId, x.MaterialId });
    }
}

public sealed class FeederLineConfiguration : IEntityTypeConfiguration<FeederLine>
{
    public void Configure(EntityTypeBuilder<FeederLine> b) => b.HasIndex(x => new { x.ProjectId, x.Kind });
}

public sealed class FeederEntryConfiguration : IEntityTypeConfiguration<FeederEntry>
{
    public void Configure(EntityTypeBuilder<FeederEntry> b) => b.Property(x => x.Notes).HasMaxLength(500);
}
