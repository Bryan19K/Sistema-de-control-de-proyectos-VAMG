using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Application.Services;

public interface IReferenceDataService
{
    Task<List<Client>> ClientsAsync(CancellationToken ct = default);
    Task<Client> GetOrCreateClientAsync(string name, CancellationToken ct = default);

    Task<List<Contractor>> ContractorsAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Contractor> SaveContractorAsync(Contractor contractor, CancellationToken ct = default);

    Task<List<Supervisor>> SupervisorsAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Supervisor> SaveSupervisorAsync(Supervisor supervisor, CancellationToken ct = default);

    Task<List<ProjectType>> ProjectTypesAsync(CancellationToken ct = default);
    Task<ProjectType> SaveProjectTypeAsync(ProjectType type, CancellationToken ct = default);

    Task<List<StageDefinition>> StageDefinitionsAsync(int projectTypeId, CancellationToken ct = default);
    Task SaveStageDefinitionAsync(StageDefinition definition, CancellationToken ct = default);
}

public interface IProjectService
{
    Task<List<Project>> ListAsync(CancellationToken ct = default);
    Task<Project?> GetAsync(int id, CancellationToken ct = default);
    Task<Project> SaveAsync(Project project, CancellationToken ct = default);
}

public interface ISiteService
{
    Task<List<SiteSummary>> ListAsync(int projectId, CancellationToken ct = default);
    Task<SiteSummary?> GetSummaryAsync(int siteId, CancellationToken ct = default);
    Task<Site?> GetAsync(int siteId, CancellationToken ct = default);
    Task<Site> SaveAsync(Site site, CancellationToken ct = default);
    Task<List<SiteStage>> StagesAsync(int siteId, CancellationToken ct = default);
    Task SetStageAsync(int siteId, StageCode code, StageStatus status, DateOnly? date, string? notes, CancellationToken ct = default);
    Task SetOdnFinalizedAsync(int siteId, bool finalized, CancellationToken ct = default);
    Task SetFeederStatusAsync(int siteId, FeederStatus status, CancellationToken ct = default);
    Task SetCaesAsync(int siteId, bool supervised, CancellationToken ct = default);
    Task SetLogsUpToDateAsync(int siteId, bool upToDate, CancellationToken ct = default);

    /// <summary>Fechas planificadas y reales de una etapa del nodo.</summary>
    Task SetStageDatesAsync(int siteId, StageCode code, DateOnly? plannedStart, DateOnly? plannedEnd, DateOnly? actualStart, DateOnly? actualEnd, CancellationToken ct = default);
}

public interface IQuantityService
{
    /// <summary>Cantidades por material de un nodo (proyectado, ajustes, entregado, instalado, devuelto).</summary>
    Task<List<MaterialQuantity>> GetSiteMaterialsAsync(int siteId, CancellationToken ct = default);

    /// <summary>Proyectado e instalado por grupo (cables, FATs, fusiones, cruces) para cada nodo del proyecto.</summary>
    Task<Dictionary<int, Dictionary<QuantityGroup, QuantityPair>>> GetGroupTotalsBySiteAsync(int projectId, CancellationToken ct = default);

    /// <summary>Total proyectado de cada material en todo el proyecto (diseño ± ajustes de todos los nodos).</summary>
    Task<Dictionary<int, decimal>> GetProjectedByMaterialAsync(int projectId, CancellationToken ct = default);
}

public interface IMaterialService
{
    Task<List<Material>> ListAsync(string? search = null, MaterialCategory? category = null, bool includeInactive = false, CancellationToken ct = default);
    Task<Material?> GetAsync(int id, CancellationToken ct = default);
    Task<Material> SaveAsync(Material material, CancellationToken ct = default);
    Task AddAliasAsync(int materialId, string alias, CancellationToken ct = default);
    Task RemoveAliasAsync(int aliasId, CancellationToken ct = default);

    /// <summary>Busca por nombre o alias ("SUB-BOX" devuelve "NAP de paso").</summary>
    Task<Material?> ResolveAsync(string name, CancellationToken ct = default);
}

public interface IUserAdminService
{
    Task<List<UserListItem>> ListAsync(CancellationToken ct = default);

    /// <summary>Nombre completo del usuario (o su correo si no lo tiene), para el campo "Elaborado por".</summary>
    Task<string> DisplayNameAsync(string userId, CancellationToken ct = default);
    Task CreateAsync(string email, string fullName, string password, IEnumerable<string> roles, CancellationToken ct = default);
    Task SetRolesAsync(string userId, IEnumerable<string> roles, CancellationToken ct = default);
    Task ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default);
    Task SetLockedAsync(string userId, bool locked, string currentUserId, CancellationToken ct = default);

    /// <summary>Teléfono para los avisos por WhatsApp. Vacío lo quita.</summary>
    Task SetPhoneAsync(string userId, string? phone, CancellationToken ct = default);
}

public sealed record FeederProgress(FeederLine Line, decimal Installed)
{
    public decimal Planned => Line.PlannedQuantity;
    public decimal Pending => Planned - Installed;
    public decimal Percent => Planned <= 0 ? 0 : Math.Round(Installed / Planned * 100m, 1);
}

public interface IFeederService
{
    /// <summary>Feeder global del proyecto: metros por tipo de fibra, cierres y ODF.</summary>
    Task<List<FeederProgress>> GetAsync(int projectId, CancellationToken ct = default);

    Task SavePlannedAsync(int projectId, FeederItemKind kind, decimal planned, CancellationToken ct = default);

    Task AddEntryAsync(int projectId, FeederItemKind kind, DateOnly date, decimal quantity, string? notes, CancellationToken ct = default);

    Task<List<FeederEntry>> EntriesAsync(int projectId, FeederItemKind kind, CancellationToken ct = default);
    Task UpdateEntryAsync(int entryId, DateOnly date, decimal quantity, string? notes, CancellationToken ct = default);
    Task RemoveEntryAsync(int entryId, CancellationToken ct = default);
}

public sealed record SprintSummary(Sprint Sprint, int Total, int Done, PaceResult Pace)
{
    public decimal ProgressPercent => Total == 0 ? 0 : Math.Round(Done * 100m / Total, 1);
}

public sealed record SprintCloseResult(int Moved, int Completed);

public interface ISprintService
{
    Task<List<SprintSummary>> ListAsync(int projectId, CancellationToken ct = default);
    Task<SprintSummary?> GetAsync(int sprintId, CancellationToken ct = default);
    Task<Sprint> SaveAsync(Sprint sprint, CancellationToken ct = default);
    Task StartAsync(int sprintId, CancellationToken ct = default);

    /// <summary>Cierra el sprint: las tareas sin terminar pasan al siguiente sprint o al backlog.</summary>
    Task<SprintCloseResult> CloseAsync(int sprintId, int? moveToSprintId, CancellationToken ct = default);
}

public sealed class TaskFilter
{
    public int ProjectId { get; set; }

    /// <summary>Null = todas; <see cref="Backlog"/> = solo las que no tienen sprint.</summary>
    public int? SprintId { get; set; }
    public bool BacklogOnly { get; set; }
    public string? AssigneeUserId { get; set; }
    public bool OnlyOpen { get; set; }
    public string? Search { get; set; }
}

public interface ITaskService
{
    Task<List<TaskItem>> ListAsync(TaskFilter filter, CancellationToken ct = default);

    /// <summary>Tareas asignadas a un usuario en todos los proyectos (vista "Mis tareas").</summary>
    Task<List<TaskItem>> MyTasksAsync(string userId, bool onlyOpen, CancellationToken ct = default);

    Task<TaskItem?> GetAsync(int taskId, CancellationToken ct = default);
    Task<TaskItem> SaveAsync(TaskItem task, CancellationToken ct = default);
    Task DeleteAsync(int taskId, CancellationToken ct = default);

    /// <summary>Mueve la tarea a otra columna (o dentro de la misma) en la posición indicada.</summary>
    Task MoveAsync(int taskId, TaskItemStatus status, int index, CancellationToken ct = default);

    Task AssignToSprintAsync(int taskId, int? sprintId, CancellationToken ct = default);

    Task AddChecklistItemAsync(int taskId, string text, CancellationToken ct = default);
    Task SetChecklistItemAsync(int itemId, bool done, CancellationToken ct = default);
    Task RemoveChecklistItemAsync(int itemId, CancellationToken ct = default);

    Task AddCommentAsync(int taskId, string text, CancellationToken ct = default);

    Task<TaskAttachment> AddAttachmentAsync(int taskId, string fileName, string contentType, Stream content, CancellationToken ct = default);
    Task<(TaskAttachment Attachment, Stream Content)?> OpenAttachmentAsync(int attachmentId, CancellationToken ct = default);
    Task RemoveAttachmentAsync(int attachmentId, CancellationToken ct = default);
}

public interface IInventoryService
{
    /// <summary>Stock calculado de cada material que pasa por bodega, con faltantes y estado.</summary>
    Task<List<StockRow>> StockAsync(int projectId, DateOnly? asOf = null, CancellationToken ct = default);

    Task<decimal> AvailableAsync(int projectId, int materialId, CancellationToken ct = default);

    Task<InventoryMovement> ReceiptAsync(int projectId, int materialId, decimal quantity, DateOnly date, string? batch, string? reference, string? notes, CancellationToken ct = default);

    /// <summary>Entrega a contratista. Se rechaza si no hay stock, salvo justificación explícita.</summary>
    Task<InventoryMovement> DeliveryAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date, int? contractorId,
        string? reference, string? notes, string? negativeStockJustification = null, CancellationToken ct = default);

    Task<InventoryMovement> ReturnAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date, string? reference, string? notes, CancellationToken ct = default);

    Task<InventoryMovement> AdjustmentAsync(int projectId, int materialId, decimal quantity, bool increase, DateOnly date, string reason, CancellationToken ct = default);

    /// <summary>Instalado en un nodo en un día (alimenta el avance y la liquidación).</summary>
    Task<InventoryMovement> InstalledAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date, int? contractorId, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Deja el instalado de un material en un nodo en el total indicado. Si sube, registra la diferencia; si baja, anula
    /// (con el motivo) los registros más recientes hasta llegar al total. Nada se borra.
    /// </summary>
    Task<decimal> SetInstalledTotalAsync(int projectId, int siteId, int materialId, decimal newTotal, DateOnly date, string? reason, CancellationToken ct = default);

    /// <summary>Deja lo entregado de un material en un nodo en el total indicado (sube con entregas nuevas; baja anulando las más recientes con motivo).</summary>
    Task<decimal> SetDeliveredTotalAsync(int projectId, int siteId, int materialId, decimal newTotal, DateOnly date, string? reason, CancellationToken ct = default);

    /// <summary>Corrige cantidad, fecha, brigada, referencia, lote y notas de un movimiento. El stock no puede quedar en negativo.</summary>
    Task<InventoryMovement> UpdateMovementAsync(int movementId, decimal quantity, DateOnly date, int? contractorId, string? reference, string? batch,
        string? notes, CancellationToken ct = default);

    /// <summary>Anula un movimiento con motivo. Nada se borra.</summary>
    Task VoidAsync(int movementId, string reason, CancellationToken ct = default);

    Task<List<InventoryMovement>> MovementsAsync(MovementFilter filter, CancellationToken ct = default);
}

public sealed record ProjectionVersionInfo(DesignProjection Projection, int Lines);

public interface IProjectionService
{
    Task<List<ProjectionVersionInfo>> VersionsAsync(int siteId, CancellationToken ct = default);
    Task<List<ProjectionAdjustment>> AdjustmentsAsync(int siteId, CancellationToken ct = default);

    /// <summary>Ajusta (suma o resta) la cantidad proyectada de un material en un nodo, siempre con motivo.</summary>
    Task<ProjectionAdjustment> AdjustAsync(int siteId, int materialId, decimal delta, string reason, DateOnly date, CancellationToken ct = default);

    /// <summary>Deja la cantidad proyectada vigente de un material en el total indicado (registra el ajuste con su motivo).</summary>
    Task<decimal> SetProjectedTotalAsync(int siteId, int materialId, decimal newTotal, string reason, DateOnly date, CancellationToken ct = default);
}

public interface ILiquidationService
{
    Task<LiquidationView> GetAsync(int siteId, CancellationToken ct = default);
    Task AddJustificationAsync(int siteId, int materialId, decimal quantity, string reason, CancellationToken ct = default);
    Task RemoveJustificationAsync(int justificationId, CancellationToken ct = default);
    Task SaveObservationsAsync(int siteId, string? observations, CancellationToken ct = default);

    /// <summary>Cierra la liquidación; se rechaza si queda material pendiente sin justificar.</summary>
    Task CloseAsync(int siteId, CancellationToken ct = default);
    Task ReopenAsync(int siteId, CancellationToken ct = default);
    Task<bool> IsClosedAsync(int siteId, CancellationToken ct = default);
}

public interface IMeasurementService
{
    Task<List<FatMeasurement>> ListAsync(int siteId, CancellationToken ct = default);

    /// <summary>Registra una medición. Si el proyecto define el rango de dBm, el resultado se califica solo.</summary>
    Task<FatMeasurement> AddAsync(int siteId, string fatCode, DateOnly date, decimal powerDbm, bool? isAcceptable, string? notes, CancellationToken ct = default);
    Task RemoveAsync(int measurementId, CancellationToken ct = default);
    Task<FatMeasurement> UpdateAsync(int measurementId, string fatCode, DateOnly date, decimal powerDbm, bool? isAcceptable, string? notes, CancellationToken ct = default);

    /// <summary>Cuenta cada FAT una sola vez, con su última medición.</summary>
    Task<MeasurementSummary> SummaryAsync(int siteId, CancellationToken ct = default);
}

public interface IWorkPlanService
{
    Task<WorkPlanView> GetAsync(int projectId, CancellationToken ct = default);

    Task<WorkPlanPhase> SavePhaseAsync(WorkPlanPhase phase, CancellationToken ct = default);
    Task DeletePhaseAsync(int phaseId, CancellationToken ct = default);

    Task<WorkPlanActivity> SaveActivityAsync(WorkPlanActivity activity, IEnumerable<int> predecessorIds, CancellationToken ct = default);
    Task DeleteActivityAsync(int activityId, CancellationToken ct = default);

    /// <summary>Cambia el avance manual y completa las fechas reales (inicia al pasar de 0, termina al llegar a 100).</summary>
    Task SetProgressAsync(int activityId, int percent, CancellationToken ct = default);

    Task<List<PlanTemplate>> TemplatesAsync(CancellationToken ct = default);

    /// <summary>Crea las fases y actividades de una plantilla. Con <paramref name="replace"/> reemplaza el plan existente.</summary>
    Task<int> ApplyTemplateAsync(int projectId, int templateId, DateOnly start, bool replace, CancellationToken ct = default);

    Task<PlanTemplate> SaveAsTemplateAsync(int projectId, string name, string? description, CancellationToken ct = default);
}

public interface IDashboardService
{
    /// <summary>Fila de un proyecto (avance, ritmo, nodos, tareas vencidas), sin importar su estado.</summary>
    Task<ProjectComparison?> ComparisonAsync(int projectId, CancellationToken ct = default);

    /// <summary>Todos los proyectos no cerrados, para compararlos.</summary>
    Task<List<ProjectComparison>> CompareAllAsync(CancellationToken ct = default);

    /// <summary>Indicadores del tablero de inicio, de todos los proyectos.</summary>
    Task<DashboardData> GetAsync(CancellationToken ct = default);

    /// <summary>Datos para los gráficos de un proyecto: instalación, nodos, tareas y feeder.</summary>
    Task<ProjectCharts> GetChartsAsync(int projectId, CancellationToken ct = default);
}

public sealed record EquipmentView(Equipment Equipment, List<EquipmentSlot> Slots, List<Transceiver> Transceivers)
{
    public int Ports => Slots.Sum(s => s.PortCount);
    public int PortsUsed => Transceivers.Count(t => t.Status == TransceiverStatus.Installed && t.PortNumber is not null);
}

public interface IEquipmentService
{
    Task<List<EquipmentView>> ListAsync(int projectId, int? siteId = null, CancellationToken ct = default);
    Task<Equipment> SaveEquipmentAsync(Equipment equipment, CancellationToken ct = default);

    /// <summary>Solo se puede quitar un equipo sin tarjetas ni SFP; si ya se usó, se marca como Retirado.</summary>
    Task RemoveEquipmentAsync(int equipmentId, CancellationToken ct = default);

    Task<EquipmentSlot> SaveSlotAsync(EquipmentSlot slot, CancellationToken ct = default);
    Task RemoveSlotAsync(int slotId, CancellationToken ct = default);

    Task<List<Transceiver>> TransceiversAsync(int projectId, string? search = null, TransceiverStatus? status = null, CancellationToken ct = default);

    /// <summary>Guarda el SFP: serial único por proyecto, puerto dentro de la tarjeta y un solo SFP instalado por puerto.</summary>
    Task<Transceiver> SaveTransceiverAsync(Transceiver transceiver, CancellationToken ct = default);
    Task RemoveTransceiverAsync(int transceiverId, CancellationToken ct = default);
}

public sealed class DocumentFilter
{
    public int ProjectId { get; set; }
    public int? SiteId { get; set; }
    public int? TaskItemId { get; set; }
    public DocumentKind? Kind { get; set; }
}

public sealed record DocumentUpload(
    int ProjectId, int? SiteId, int? TaskItemId, DocumentKind Kind, string Title, DateOnly Date,
    bool IsSigned, string? Notes, string FileName, string ContentType);

public interface IDocumentService
{
    Task<List<ProjectDocument>> ListAsync(DocumentFilter filter, CancellationToken ct = default);
    Task<ProjectDocument> UploadAsync(DocumentUpload upload, Stream content, CancellationToken ct = default);
    Task UpdateAsync(int documentId, string title, DocumentKind kind, DateOnly date, bool isSigned, string? notes, int? siteId, int? taskItemId, CancellationToken ct = default);
    Task<(ProjectDocument Document, Stream Content)?> OpenAsync(int documentId, CancellationToken ct = default);

    /// <summary>Quita el documento de las listas (borrado lógico); el archivo se conserva.</summary>
    Task RemoveAsync(int documentId, CancellationToken ct = default);
}

public interface IMinuteService
{
    Task<List<Minute>> ListAsync(int projectId, CancellationToken ct = default);
    Task<Minute?> GetAsync(int minuteId, CancellationToken ct = default);

    /// <summary>Guarda la minuta y sincroniza sus pendientes: agrega los nuevos, actualiza los existentes y quita los que ya no están.</summary>
    Task<Minute> SaveAsync(Minute minute, CancellationToken ct = default);
    Task SetItemDoneAsync(int itemId, bool done, CancellationToken ct = default);

    /// <summary>Pendientes sin cerrar de todas las minutas del proyecto, opcionalmente de un nodo.</summary>
    Task<List<MinuteItem>> PendingAsync(int projectId, int? siteId = null, CancellationToken ct = default);
}

public interface IPresentationService
{
    /// <summary>Todos los datos de los gráficos de la sección Presentación, de un proyecto.</summary>
    Task<PresentationData> GetAsync(int projectId, CancellationToken ct = default);
}

public interface ISnapshotService
{
    /// <summary>Guarda (o actualiza) la foto de hoy de un proyecto.</summary>
    Task<ProgressSnapshot?> CaptureAsync(int projectId, CancellationToken ct = default);

    /// <summary>Foto de hoy de todos los proyectos no cerrados. Lo corre el trabajo diario.</summary>
    Task<int> CaptureAllAsync(CancellationToken ct = default);

    Task<List<ProgressSnapshot>> ListAsync(int projectId, CancellationToken ct = default);
}

public interface IAttentionService
{
    /// <summary>Lo que necesita atención ya: nodos atrasados, tareas vencidas, stock bajo, pendientes y evidencias que faltan.</summary>
    Task<List<AttentionItem>> GetAsync(int projectId, CancellationToken ct = default);
}

public interface IFinanceService
{
    Task<ExpenseSummary> SummaryAsync(CancellationToken ct = default);
    Task<List<ExpenseEntry>> ExpensesAsync(ExpenseKind? kind, int? fundId, CancellationToken ct = default);
    Task<List<ContractOrder>> OrdersAsync(int projectId, CancellationToken ct = default);
    Task<List<PriceItem>> PricesAsync(int projectId, CancellationToken ct = default);

    // Edición (administrador): lo que viene del Excel se puede corregir, agregar y quitar desde el sistema.
    Task<ExpenseEntry> SaveExpenseAsync(ExpenseEntry entry, CancellationToken ct = default);
    Task DeleteExpenseAsync(int id, CancellationToken ct = default);
    Task<List<ExpenseFund>> FundsAsync(CancellationToken ct = default);
    Task<ExpenseFund> SaveFundAsync(ExpenseFund fund, CancellationToken ct = default);
    Task DeleteFundAsync(int id, CancellationToken ct = default);
    Task<ContractOrder> SaveOrderAsync(ContractOrder order, CancellationToken ct = default);
    Task DeleteOrderAsync(int id, CancellationToken ct = default);
    Task<PriceItem> SavePriceAsync(PriceItem price, CancellationToken ct = default);
    Task DeletePriceAsync(int id, CancellationToken ct = default);
}

public interface IBuildingService
{
    Task<List<BuildingOverview>> OverviewAsync(CancellationToken ct = default);
    Task<List<BuildingRecord>> RecordsAsync(int projectId, BuildingRecordKind kind, CancellationToken ct = default);
    Task<List<BuildingMaterialRow>> MaterialsAsync(int projectId, CancellationToken ct = default);

    // Edición (administrador)
    Task<Project> SaveProjectAsync(int projectId, decimal? reportedProgress, string? pendingWork, ProjectStatus status, string? responsible,
        DateOnly? plannedStart, DateOnly? plannedEnd, CancellationToken ct = default);
    Task<ProjectPhase> SavePhaseAsync(ProjectPhase phase, CancellationToken ct = default);
    Task DeletePhaseAsync(int id, CancellationToken ct = default);
    Task<BuildingRecord> SaveRecordAsync(BuildingRecord record, CancellationToken ct = default);
    Task DeleteRecordAsync(int id, CancellationToken ct = default);
}

public interface IContractorDirectoryService
{
    Task<List<ContractorView>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<Contractor?> GetAsync(int id, CancellationToken ct = default);
    Task<Contractor> SaveAsync(Contractor contractor, CancellationToken ct = default);
    Task SetActiveAsync(int id, bool active, CancellationToken ct = default);

    /// <summary>Personal de campo con el estado de su póliza o afiliación. Se puede filtrar por contratista, estado o texto.</summary>
    Task<List<PersonView>> PeopleAsync(int? contractorId = null, CoverageState? state = null, string? search = null, bool onlyNeedingAttention = false, CancellationToken ct = default);

    Task<ContractorPerson> SavePersonAsync(ContractorPerson person, CancellationToken ct = default);
    Task RemovePersonAsync(int personId, CancellationToken ct = default);
}

public interface ILoadVerificationService
{
    /// <summary>Revisa que todo lo que traían los Excel de TEK-COM esté cargado en el sistema y lo compara con las cifras de cada archivo.</summary>
    Task<LoadVerification> VerifyAsync(CancellationToken ct = default);
}
