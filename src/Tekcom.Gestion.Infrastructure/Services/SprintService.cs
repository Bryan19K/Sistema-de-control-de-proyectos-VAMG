using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class SprintService(IDbContextFactory<ApplicationDbContext> factory, IDateTimeProvider clock) : ISprintService
{
    public async Task<List<SprintSummary>> ListAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var margin = await MarginAsync(db, projectId, ct);
        var sprints = await db.Sprints.AsNoTracking()
            .Include(s => s.Tasks)
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.StartDate)
            .ToListAsync(ct);

        return sprints.Select(s => Summarize(s, margin)).ToList();
    }

    public async Task<SprintSummary?> GetAsync(int sprintId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var sprint = await db.Sprints.AsNoTracking().Include(s => s.Tasks).FirstOrDefaultAsync(s => s.Id == sprintId, ct);
        return sprint is null ? null : Summarize(sprint, await MarginAsync(db, sprint.ProjectId, ct));
    }

    public async Task<Sprint> SaveAsync(Sprint sprint, CancellationToken ct = default)
    {
        var name = (sprint.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre del sprint es obligatorio.");
        }

        if (sprint.EndDate < sprint.StartDate)
        {
            throw new BusinessRuleException("La fecha final del sprint no puede ser anterior a la inicial.");
        }

        await using var db = factory.CreateDbContext();
        if (!await db.Projects.AnyAsync(p => p.Id == sprint.ProjectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }

        var entity = sprint.Id == 0
            ? db.Sprints.Add(new Sprint { ProjectId = sprint.ProjectId }).Entity
            : await db.Sprints.FirstAsync(s => s.Id == sprint.Id, ct);

        if (entity.Status == SprintStatus.Closed)
        {
            throw new BusinessRuleException("Un sprint cerrado no se puede modificar.");
        }

        entity.Name = name;
        entity.Goal = string.IsNullOrWhiteSpace(sprint.Goal) ? null : sprint.Goal.Trim();
        entity.StartDate = sprint.StartDate;
        entity.EndDate = sprint.EndDate;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task StartAsync(int sprintId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == sprintId, ct)
            ?? throw new BusinessRuleException("El sprint no existe.");
        if (sprint.Status != SprintStatus.Planned)
        {
            throw new BusinessRuleException("Solo se puede iniciar un sprint planificado.");
        }

        var active = await db.Sprints.FirstOrDefaultAsync(s => s.ProjectId == sprint.ProjectId && s.Status == SprintStatus.Active, ct);
        if (active is not null)
        {
            throw new BusinessRuleException($"Ya hay un sprint activo ('{active.Name}'). Ciérrelo antes de iniciar otro.");
        }

        sprint.Status = SprintStatus.Active;
        await db.SaveChangesAsync(ct);
    }

    public async Task<SprintCloseResult> CloseAsync(int sprintId, int? moveToSprintId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var sprint = await db.Sprints.Include(s => s.Tasks).FirstOrDefaultAsync(s => s.Id == sprintId, ct)
            ?? throw new BusinessRuleException("El sprint no existe.");
        if (sprint.Status == SprintStatus.Closed)
        {
            throw new BusinessRuleException("El sprint ya está cerrado.");
        }

        if (moveToSprintId is { } targetId)
        {
            var target = await db.Sprints.FirstOrDefaultAsync(s => s.Id == targetId, ct)
                ?? throw new BusinessRuleException("El sprint de destino no existe.");
            if (target.ProjectId != sprint.ProjectId || target.Status == SprintStatus.Closed || target.Id == sprint.Id)
            {
                throw new BusinessRuleException("El sprint de destino no es válido.");
            }
        }

        var pending = sprint.Tasks.Where(t => t.Status != TaskItemStatus.Done).ToList();
        foreach (var task in pending)
        {
            task.SprintId = moveToSprintId;
        }

        sprint.Status = SprintStatus.Closed;
        sprint.ClosedOn = clock.Today;
        await db.SaveChangesAsync(ct);
        return new SprintCloseResult(pending.Count, sprint.Tasks.Count(t => t.Status == TaskItemStatus.Done));
    }

    private static async Task<int> MarginAsync(ApplicationDbContext db, int projectId, CancellationToken ct) =>
        await db.Projects.Where(p => p.Id == projectId).Select(p => p.PaceMarginPercent).FirstOrDefaultAsync(ct) is var margin and > 0
            ? margin : 10;

    private SprintSummary Summarize(Sprint sprint, int margin)
    {
        var total = sprint.Tasks.Count;
        var done = sprint.Tasks.Count(t => t.Status == TaskItemStatus.Done);
        var progress = total == 0 ? 0 : done * 100m / total;
        var pace = PaceCalculator.Calculate(progress, sprint.StartDate, sprint.EndDate, clock.Today, margin);
        return new SprintSummary(sprint, total, done, pace);
    }
}
