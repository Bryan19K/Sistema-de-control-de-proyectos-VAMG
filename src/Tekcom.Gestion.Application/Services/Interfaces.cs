using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;

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
