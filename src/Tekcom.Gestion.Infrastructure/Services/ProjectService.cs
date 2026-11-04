using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class ProjectService(IDbContextFactory<ApplicationDbContext> factory) : IProjectService
{
    public async Task<List<Project>> ListAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Projects.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.ProjectType)
            .OrderBy(p => p.Status == Domain.Enums.ProjectStatus.Closed)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<Project?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Projects.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.ProjectType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Project> SaveAsync(Project project, CancellationToken ct = default)
    {
        var code = (project.Code ?? string.Empty).Trim();
        var name = (project.Name ?? string.Empty).Trim();
        if (code.Length == 0 || name.Length == 0)
        {
            throw new BusinessRuleException("El código y el nombre del proyecto son obligatorios.");
        }

        if (project.PlannedStart is not null && project.PlannedEnd is not null && project.PlannedEnd < project.PlannedStart)
        {
            throw new BusinessRuleException("La fecha final planificada no puede ser anterior a la inicial.");
        }

        if (project.PaceMarginPercent is < 0 or > 50)
        {
            throw new BusinessRuleException("El margen del indicador de ritmo debe estar entre 0 y 50 puntos.");
        }

        if (project.ReadyToMeasurePercent is < 1 or > 100)
        {
            throw new BusinessRuleException("El porcentaje de FATs para medir debe estar entre 1 y 100.");
        }

        if (project.MinAcceptableDbm is not null && project.MaxAcceptableDbm is not null
            && project.MinAcceptableDbm > project.MaxAcceptableDbm)
        {
            throw new BusinessRuleException("El mínimo de dBm no puede ser mayor que el máximo.");
        }

        await using var db = factory.CreateDbContext();
        if (!await db.ProjectTypes.AnyAsync(t => t.Id == project.ProjectTypeId, ct))
        {
            throw new BusinessRuleException("Seleccione un tipo de proyecto.");
        }

        if (await db.Projects.AnyAsync(p => p.Id != project.Id && p.Code == code, ct))
        {
            throw new BusinessRuleException($"Ya existe un proyecto con el código '{code}'.");
        }

        var entity = project.Id == 0
            ? db.Projects.Add(new Project()).Entity
            : await db.Projects.FirstAsync(p => p.Id == project.Id, ct);

        entity.Code = code;
        entity.Name = name;
        entity.Description = string.IsNullOrWhiteSpace(project.Description) ? null : project.Description.Trim();
        entity.ClientId = project.ClientId;
        entity.ProjectTypeId = project.ProjectTypeId;
        entity.Status = project.Status;
        entity.PlannedStart = project.PlannedStart;
        entity.PlannedEnd = project.PlannedEnd;
        entity.ActualStart = project.ActualStart;
        entity.ActualEnd = project.ActualEnd;
        entity.ResponsibleName = string.IsNullOrWhiteSpace(project.ResponsibleName) ? null : project.ResponsibleName.Trim();
        entity.PaceMarginPercent = project.PaceMarginPercent;
        entity.ReadyToMeasurePercent = project.ReadyToMeasurePercent;
        entity.MinAcceptableDbm = project.MinAcceptableDbm;
        entity.MaxAcceptableDbm = project.MaxAcceptableDbm;

        await db.SaveChangesAsync(ct);
        return entity;
    }
}
