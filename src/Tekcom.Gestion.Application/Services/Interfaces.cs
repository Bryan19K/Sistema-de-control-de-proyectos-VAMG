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
    Task CreateAsync(string email, string fullName, string password, IEnumerable<string> roles, CancellationToken ct = default);
    Task SetRolesAsync(string userId, IEnumerable<string> roles, CancellationToken ct = default);
    Task ResetPasswordAsync(string userId, string newPassword, CancellationToken ct = default);
    Task SetLockedAsync(string userId, bool locked, string currentUserId, CancellationToken ct = default);
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
    Task<List<StockRow>> StockAsync(int projectId, CancellationToken ct = default);

    Task<decimal> AvailableAsync(int projectId, int materialId, CancellationToken ct = default);

    Task<InventoryMovement> ReceiptAsync(int projectId, int materialId, decimal quantity, DateOnly date, string? batch, string? reference, string? notes, CancellationToken ct = default);

    /// <summary>Entrega a contratista. Se rechaza si no hay stock, salvo justificación explícita.</summary>
    Task<InventoryMovement> DeliveryAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date, int? contractorId,
        string? reference, string? notes, string? negativeStockJustification = null, CancellationToken ct = default);

    Task<InventoryMovement> ReturnAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date, string? reference, string? notes, CancellationToken ct = default);

    Task<InventoryMovement> AdjustmentAsync(int projectId, int materialId, decimal quantity, bool increase, DateOnly date, string reason, CancellationToken ct = default);

    /// <summary>Instalado en un nodo en un día (alimenta el avance y la liquidación).</summary>
    Task<InventoryMovement> InstalledAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date, int? contractorId, string? notes, CancellationToken ct = default);

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

    /// <summary>Cuenta cada FAT una sola vez, con su última medición.</summary>
    Task<MeasurementSummary> SummaryAsync(int siteId, CancellationToken ct = default);
}
