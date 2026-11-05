using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class ReferenceDataService(IDbContextFactory<ApplicationDbContext> factory) : IReferenceDataService
{
    public async Task<List<Client>> ClientsAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Clients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Client> GetOrCreateClientAsync(string name, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del cliente es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var key = NameNormalizer.Key(name);
        var existing = (await db.Clients.ToListAsync(ct)).FirstOrDefault(c => NameNormalizer.Key(c.Name) == key);
        if (existing is not null)
        {
            return existing;
        }

        var client = new Client { Name = name };
        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);
        return client;
    }

    public async Task<Client> SaveClientAsync(int id, string name, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del cliente es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var key = NameNormalizer.Key(name);
        var all = await db.Clients.ToListAsync(ct);
        if (all.Any(c => c.Id != id && NameNormalizer.Key(c.Name) == key))
        {
            throw new BusinessRuleException($"Ya existe un cliente llamado '{name}'.");
        }

        Client client;
        if (id == 0)
        {
            client = new Client();
            db.Clients.Add(client);
        }
        else
        {
            client = all.FirstOrDefault(c => c.Id == id) ?? throw new BusinessRuleException("El cliente no existe.");
        }

        client.Name = name;
        await db.SaveChangesAsync(ct);
        return client;
    }

    public async Task DeleteClientAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var used = await db.Projects.CountAsync(p => p.ClientId == id, ct);
        if (used > 0)
        {
            throw new BusinessRuleException($"El cliente tiene {used} proyecto(s): cámbieles el cliente antes de quitarlo.");
        }

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new BusinessRuleException("El cliente no existe.");
        db.Clients.Remove(client);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Contractor>> ContractorsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Contractors.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<Contractor> SaveContractorAsync(Contractor contractor, CancellationToken ct = default)
    {
        var name = (contractor.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del contratista es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var key = NameNormalizer.Key(name);
        var duplicate = (await db.Contractors.ToListAsync(ct))
            .Any(c => c.Id != contractor.Id && NameNormalizer.Key(c.Name) == key);
        if (duplicate)
        {
            throw new BusinessRuleException($"Ya existe un contratista llamado '{name}'.");
        }

        var entity = contractor.Id == 0
            ? db.Contractors.Add(new Contractor()).Entity
            : await db.Contractors.FirstAsync(c => c.Id == contractor.Id, ct);

        entity.Name = name;
        entity.ResponsibleName = Clean(contractor.ResponsibleName);
        entity.Phone = Clean(contractor.Phone);
        entity.IsActive = contractor.IsActive;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<List<Supervisor>> SupervisorsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Supervisors.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<Supervisor> SaveSupervisorAsync(Supervisor supervisor, CancellationToken ct = default)
    {
        var name = (supervisor.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del supervisor es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var key = NameNormalizer.Key(name);
        var duplicate = (await db.Supervisors.ToListAsync(ct))
            .Any(s => s.Id != supervisor.Id && NameNormalizer.Key(s.Name) == key);
        if (duplicate)
        {
            throw new BusinessRuleException($"Ya existe un supervisor llamado '{name}'.");
        }

        var entity = supervisor.Id == 0
            ? db.Supervisors.Add(new Supervisor()).Entity
            : await db.Supervisors.FirstAsync(s => s.Id == supervisor.Id, ct);

        entity.Name = name;
        entity.Phone = Clean(supervisor.Phone);
        entity.IsActive = supervisor.IsActive;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<List<ProjectType>> ProjectTypesAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.ProjectTypes.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<ProjectType> SaveProjectTypeAsync(ProjectType type, CancellationToken ct = default)
    {
        var name = (type.Name ?? string.Empty).Trim();
        var code = (type.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (name.Length == 0 || code.Length == 0)
        {
            throw new BusinessRuleException("El código y el nombre del tipo de proyecto son obligatorios.");
        }

        await using var db = factory.CreateDbContext();
        if (await db.ProjectTypes.AnyAsync(t => t.Id != type.Id && t.Code == code, ct))
        {
            throw new BusinessRuleException($"Ya existe un tipo de proyecto con el código '{code}'.");
        }

        var entity = type.Id == 0
            ? db.ProjectTypes.Add(new ProjectType()).Entity
            : await db.ProjectTypes.FirstAsync(t => t.Id == type.Id, ct);

        entity.Name = name;
        if (!entity.IsSystem)
        {
            entity.Code = code;
            entity.Modules = type.Modules;
        }

        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<List<StageDefinition>> StageDefinitionsAsync(int projectTypeId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.StageDefinitions.AsNoTracking()
            .Where(d => d.ProjectTypeId == projectTypeId)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(ct);
    }

    public async Task SaveStageDefinitionAsync(StageDefinition definition, CancellationToken ct = default)
    {
        if (definition.PlannedDays < 0)
        {
            throw new BusinessRuleException("Los días planificados no pueden ser negativos.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new BusinessRuleException("El nombre de la etapa es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var entity = await db.StageDefinitions.FirstAsync(d => d.Id == definition.Id, ct);
        entity.Name = definition.Name.Trim();
        entity.PlannedDays = definition.PlannedDays;
        entity.CountsForProgress = definition.CountsForProgress;
        await db.SaveChangesAsync(ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
