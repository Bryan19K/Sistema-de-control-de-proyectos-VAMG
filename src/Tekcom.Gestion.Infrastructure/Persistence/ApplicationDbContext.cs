using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Identity;

namespace Tekcom.Gestion.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    // Referencias
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ProjectType> ProjectTypes => Set<ProjectType>();
    public DbSet<Supervisor> Supervisors => Set<Supervisor>();
    public DbSet<Contractor> Contractors => Set<Contractor>();
    public DbSet<ContractorPerson> ContractorPeople => Set<ContractorPerson>();

    // Proyectos y nodos
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<StageDefinition> StageDefinitions => Set<StageDefinition>();
    public DbSet<SiteStage> SiteStages => Set<SiteStage>();

    // Materiales e inventario
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialAlias> MaterialAliases => Set<MaterialAlias>();
    public DbSet<DesignProjection> DesignProjections => Set<DesignProjection>();
    public DbSet<DesignProjectionLine> DesignProjectionLines => Set<DesignProjectionLine>();
    public DbSet<ProjectionAdjustment> ProjectionAdjustments => Set<ProjectionAdjustment>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    // Feeder
    public DbSet<FeederLine> FeederLines => Set<FeederLine>();
    public DbSet<FeederEntry> FeederEntries => Set<FeederEntry>();

    // Plan de trabajo
    public DbSet<WorkPlanPhase> WorkPlanPhases => Set<WorkPlanPhase>();
    public DbSet<WorkPlanActivity> WorkPlanActivities => Set<WorkPlanActivity>();
    public DbSet<ActivityDependency> ActivityDependencies => Set<ActivityDependency>();
    public DbSet<PlanTemplate> PlanTemplates => Set<PlanTemplate>();
    public DbSet<Equipment> EquipmentItems => Set<Equipment>();
    public DbSet<EquipmentSlot> EquipmentSlots => Set<EquipmentSlot>();
    public DbSet<Transceiver> Transceivers => Set<Transceiver>();
    public DbSet<ProjectDocument> Documents => Set<ProjectDocument>();
    public DbSet<ProgressSnapshot> ProgressSnapshots => Set<ProgressSnapshot>();
    public DbSet<Minute> Minutes => Set<Minute>();
    public DbSet<MinuteItem> MinuteItems => Set<MinuteItem>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    // Edificios (proyectos NextWave)
    public DbSet<ProjectPhase> ProjectPhases => Set<ProjectPhase>();
    public DbSet<BuildingRecord> BuildingRecords => Set<BuildingRecord>();

    // Gastos y precios
    public DbSet<ExpenseFund> ExpenseFunds => Set<ExpenseFund>();
    public DbSet<ExpenseEntry> ExpenseEntries => Set<ExpenseEntry>();
    public DbSet<PriceItem> PriceItems => Set<PriceItem>();
    public DbSet<ContractOrder> ContractOrders => Set<ContractOrder>();

    // Ajustes del sistema
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // Exportes
    public DbSet<ExportTemplate> ExportTemplates => Set<ExportTemplate>();

    // Liquidación y mediciones
    public DbSet<Liquidation> Liquidations => Set<Liquidation>();
    public DbSet<LiquidationJustification> LiquidationJustifications => Set<LiquidationJustification>();
    public DbSet<FatMeasurement> FatMeasurements => Set<FatMeasurement>();

    // Sprints y tareas
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskChecklistItem> TaskChecklistItems => Set<TaskChecklistItem>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        builder.UseRestrictDeleteBehavior();
        builder.UseDecimalPrecision();
        builder.ApplyAuditConventions();
        builder.ApplySoftDeleteQueryFilter();
    }
}
