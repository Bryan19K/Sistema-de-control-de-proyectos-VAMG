using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>
/// Plan de trabajo: fases, actividades con dependencias e hitos. El avance de una actividad vinculada a un
/// nodo es el del nodo; el de una fase y el del plan se ponderan por la duración de sus actividades.
/// </summary>
public sealed class WorkPlanService(
    IDbContextFactory<ApplicationDbContext> factory,
    ISiteService sites,
    IDateTimeProvider clock) : IWorkPlanService
{
    public async Task<WorkPlanView> GetAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var margin = await db.Projects.Where(p => p.Id == projectId).Select(p => p.PaceMarginPercent).FirstOrDefaultAsync(ct);
        margin = margin <= 0 ? 10 : margin;

        var phases = await db.WorkPlanPhases.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .ToListAsync(ct);
        var activities = await db.WorkPlanActivities.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.PlannedStart).ThenBy(a => a.Id)
            .ToListAsync(ct);
        var dependencies = await db.ActivityDependencies.AsNoTracking()
            .Where(d => activities.Select(a => a.Id).Contains(d.ActivityId))
            .ToListAsync(ct);

        var siteProgress = new Dictionary<int, decimal>();
        if (activities.Any(a => a.SiteId is not null))
        {
            foreach (var summary in await sites.ListAsync(projectId, ct))
            {
                siteProgress[summary.Site.Id] = summary.Site.OdnFinalized ? 100 : summary.ProgressPercent;
            }
        }

        var byId = activities.ToDictionary(a => a.Id);
        var phaseViews = new List<PlanPhaseView>();
        foreach (var phase in phases)
        {
            var views = activities.Where(a => a.PhaseId == phase.Id).Select(a =>
            {
                var progress = a.SiteId is { } siteId && siteProgress.TryGetValue(siteId, out var p)
                    ? p
                    : a.IsMilestone && a.ActualEnd is not null ? 100 : a.ProgressPercent;
                var predecessors = dependencies.Where(d => d.ActivityId == a.Id && byId.ContainsKey(d.PredecessorId))
                    .Select(d => byId[d.PredecessorId]).ToList();
                return new PlanActivityView
                {
                    Activity = a,
                    PhaseName = phase.Name,
                    Progress = progress,
                    Pace = ActivityPace(a, progress, margin),
                    Predecessors = predecessors,
                    StartsBeforePredecessor = predecessors.Any(pre => pre.PlannedEnd >= a.PlannedStart),
                };
            }).ToList();

            var phaseProgress = Weighted(views);
            phaseViews.Add(new PlanPhaseView
            {
                Phase = phase,
                Activities = views,
                Progress = phaseProgress,
                Pace = Pace(phaseProgress, views.Count == 0 ? null : views.Min(v => v.Activity.PlannedStart),
                    views.Count == 0 ? null : views.Max(v => v.Activity.PlannedEnd), margin),
            });
        }

        var all = phaseViews.SelectMany(p => p.Activities).ToList();
        var planProgress = Weighted(all);
        return new WorkPlanView
        {
            Phases = phaseViews,
            Progress = planProgress,
            Pace = Pace(planProgress, all.Count == 0 ? null : all.Min(a => a.Activity.PlannedStart),
                all.Count == 0 ? null : all.Max(a => a.Activity.PlannedEnd), margin),
        };
    }

    public async Task<WorkPlanPhase> SavePhaseAsync(WorkPlanPhase phase, CancellationToken ct = default)
    {
        var name = (phase.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre de la fase es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        if (!await db.Projects.AnyAsync(p => p.Id == phase.ProjectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }

        WorkPlanPhase entity;
        if (phase.Id == 0)
        {
            var max = await db.WorkPlanPhases.Where(p => p.ProjectId == phase.ProjectId).Select(p => (int?)p.SortOrder).MaxAsync(ct);
            entity = db.WorkPlanPhases.Add(new WorkPlanPhase { ProjectId = phase.ProjectId, SortOrder = (max ?? 0) + 1 }).Entity;
        }
        else
        {
            entity = await db.WorkPlanPhases.FirstAsync(p => p.Id == phase.Id, ct);
        }

        entity.Name = name;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeletePhaseAsync(int phaseId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var phase = await db.WorkPlanPhases.FirstOrDefaultAsync(p => p.Id == phaseId, ct);
        if (phase is null)
        {
            return;
        }

        var activityIds = await db.WorkPlanActivities.Where(a => a.PhaseId == phaseId).Select(a => a.Id).ToListAsync(ct);
        await RemoveActivitiesAsync(db, activityIds, ct);
        db.WorkPlanPhases.Remove(phase);
        await db.SaveChangesAsync(ct);
    }

    public async Task<WorkPlanActivity> SaveActivityAsync(WorkPlanActivity activity, IEnumerable<int> predecessorIds, CancellationToken ct = default)
    {
        var name = (activity.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre de la actividad es obligatorio.");
        }

        if (activity.PlannedEnd < activity.PlannedStart && !activity.IsMilestone)
        {
            throw new BusinessRuleException("La fecha final no puede ser anterior a la inicial.");
        }

        if (activity.ActualStart is not null && activity.ActualEnd is not null && activity.ActualEnd < activity.ActualStart)
        {
            throw new BusinessRuleException("El fin real no puede ser anterior al inicio real.");
        }

        if (activity.ProgressPercent is < 0 or > 100)
        {
            throw new BusinessRuleException("El avance debe estar entre 0 y 100.");
        }

        await using var db = factory.CreateDbContext();
        var phase = await db.WorkPlanPhases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == activity.PhaseId, ct)
            ?? throw new BusinessRuleException("Seleccione una fase.");

        if (activity.SiteId is { } siteId && !await db.Sites.AnyAsync(s => s.Id == siteId && s.ProjectId == phase.ProjectId, ct))
        {
            throw new BusinessRuleException("El nodo no pertenece a este proyecto.");
        }

        var predecessors = predecessorIds.Distinct().ToList();
        if (activity.Id != 0 && predecessors.Contains(activity.Id))
        {
            throw new BusinessRuleException("Una actividad no puede depender de sí misma.");
        }

        var projectActivityIds = await db.WorkPlanActivities.Where(a => a.ProjectId == phase.ProjectId).Select(a => a.Id).ToListAsync(ct);
        if (predecessors.Any(id => !projectActivityIds.Contains(id)))
        {
            throw new BusinessRuleException("Las predecesoras deben ser actividades de este proyecto.");
        }

        if (activity.Id != 0)
        {
            await EnsureNoCycleAsync(db, activity.Id, predecessors, ct);
        }

        WorkPlanActivity entity;
        if (activity.Id == 0)
        {
            var max = await db.WorkPlanActivities.Where(a => a.PhaseId == phase.Id).Select(a => (int?)a.SortOrder).MaxAsync(ct);
            entity = db.WorkPlanActivities.Add(new WorkPlanActivity { ProjectId = phase.ProjectId, SortOrder = (max ?? 0) + 1 }).Entity;
        }
        else
        {
            entity = await db.WorkPlanActivities.Include(a => a.Predecessors).FirstAsync(a => a.Id == activity.Id, ct);
        }

        entity.PhaseId = phase.Id;
        entity.Name = name;
        entity.IsMilestone = activity.IsMilestone;
        entity.PlannedStart = activity.PlannedStart;
        entity.PlannedEnd = activity.IsMilestone ? activity.PlannedStart : activity.PlannedEnd;
        entity.ActualStart = activity.ActualStart;
        entity.ActualEnd = activity.ActualEnd;
        entity.ProgressPercent = activity.ProgressPercent;
        entity.SiteId = activity.SiteId;
        entity.Notes = string.IsNullOrWhiteSpace(activity.Notes) ? null : activity.Notes.Trim();
        await db.SaveChangesAsync(ct);

        // Dependencias: se reemplaza el conjunto.
        var existing = await db.ActivityDependencies.Where(d => d.ActivityId == entity.Id).ToListAsync(ct);
        foreach (var dependency in existing.Where(d => !predecessors.Contains(d.PredecessorId)))
        {
            db.ActivityDependencies.Remove(dependency);
        }

        foreach (var id in predecessors.Where(id => existing.All(d => d.PredecessorId != id)))
        {
            db.ActivityDependencies.Add(new ActivityDependency { ActivityId = entity.Id, PredecessorId = id });
        }

        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteActivityAsync(int activityId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        await RemoveActivitiesAsync(db, [activityId], ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetProgressAsync(int activityId, int percent, CancellationToken ct = default)
    {
        if (percent is < 0 or > 100)
        {
            throw new BusinessRuleException("El avance debe estar entre 0 y 100.");
        }

        await using var db = factory.CreateDbContext();
        var activity = await db.WorkPlanActivities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
            ?? throw new BusinessRuleException("La actividad no existe.");

        activity.ProgressPercent = percent;
        if (percent > 0)
        {
            activity.ActualStart ??= clock.Today;
        }
        else
        {
            activity.ActualStart = null;
        }

        activity.ActualEnd = percent >= 100 ? activity.ActualEnd ?? clock.Today : null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<PlanTemplate>> TemplatesAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.PlanTemplates.AsNoTracking().OrderByDescending(t => t.IsSystem).ThenBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<int> ApplyTemplateAsync(int projectId, int templateId, DateOnly start, bool replace, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }

        var template = await db.PlanTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new BusinessRuleException("La plantilla no existe.");
        var definition = PlanTemplateDefinition.FromJson(template.DefinitionJson);
        if (definition.Phases.Count == 0)
        {
            throw new BusinessRuleException("La plantilla está vacía.");
        }

        var hasPlan = await db.WorkPlanPhases.AnyAsync(p => p.ProjectId == projectId, ct);
        if (hasPlan && !replace)
        {
            throw new BusinessRuleException("El proyecto ya tiene un plan de trabajo. Elija reemplazarlo para aplicar la plantilla.");
        }

        if (hasPlan)
        {
            var oldPhases = await db.WorkPlanPhases.Where(p => p.ProjectId == projectId).ToListAsync(ct);
            var oldActivityIds = await db.WorkPlanActivities.Where(a => a.ProjectId == projectId).Select(a => a.Id).ToListAsync(ct);
            await RemoveActivitiesAsync(db, oldActivityIds, ct);
            db.WorkPlanPhases.RemoveRange(oldPhases);
            await db.SaveChangesAsync(ct);
        }

        var projectSites = await db.Sites.AsNoTracking().Where(s => s.ProjectId == projectId).OrderBy(s => s.Code).ToListAsync(ct);
        var created = 0;
        var order = 1;
        foreach (var phaseTemplate in definition.Phases)
        {
            var phase = new WorkPlanPhase { ProjectId = projectId, Name = phaseTemplate.Name, SortOrder = order++ };
            db.WorkPlanPhases.Add(phase);

            var activityOrder = 1;
            foreach (var item in phaseTemplate.Activities)
            {
                var targets = item.PerSite
                    ? projectSites.Select(s => (Site: (Site?)s, Name: item.Name.Replace("{nodo}", s.Code))).ToList()
                    : [((Site?)null, item.Name)];

                foreach (var (site, name) in targets)
                {
                    var first = start.AddDays(item.StartDay);
                    phase.Activities.Add(new WorkPlanActivity
                    {
                        ProjectId = projectId,
                        Name = name,
                        SortOrder = activityOrder++,
                        PlannedStart = first,
                        PlannedEnd = item.Milestone ? first : first.AddDays(Math.Max(1, item.DurationDays) - 1),
                        IsMilestone = item.Milestone,
                        SiteId = site?.Id,
                    });
                    created++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return created;
    }

    public async Task<PlanTemplate> SaveAsTemplateAsync(int projectId, string name, string? description, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre de la plantilla es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        if (await db.PlanTemplates.AnyAsync(t => t.Name == name, ct))
        {
            throw new BusinessRuleException($"Ya existe una plantilla llamada '{name}'.");
        }

        var phases = await db.WorkPlanPhases.AsNoTracking().Where(p => p.ProjectId == projectId).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var activities = await db.WorkPlanActivities.AsNoTracking().Include(a => a.Site).Where(a => a.ProjectId == projectId).ToListAsync(ct);
        if (activities.Count == 0)
        {
            throw new BusinessRuleException("El proyecto no tiene actividades para guardar como plantilla.");
        }

        var planStart = activities.Min(a => a.PlannedStart);
        var planEnd = activities.Max(a => a.PlannedEnd);
        var templatePhases = new List<PlanTemplatePhase>();
        foreach (var phase in phases)
        {
            var items = new List<PlanTemplateActivity>();
            foreach (var a in activities.Where(a => a.PhaseId == phase.Id).OrderBy(a => a.SortOrder))
            {
                var perSite = a.Site is not null;
                var activityName = perSite ? a.Name.Replace(a.Site!.Code, "{nodo}", StringComparison.OrdinalIgnoreCase) : a.Name;
                var item = new PlanTemplateActivity(activityName, a.PlannedStart.DayNumber - planStart.DayNumber,
                    a.IsMilestone ? 0 : a.PlannedEnd.DayNumber - a.PlannedStart.DayNumber + 1, a.IsMilestone, perSite);
                if (!items.Contains(item))
                {
                    items.Add(item);
                }
            }

            if (items.Count > 0)
            {
                templatePhases.Add(new PlanTemplatePhase(phase.Name, items));
            }
        }

        var definition = new PlanTemplateDefinition((int)Math.Ceiling((planEnd.DayNumber - planStart.DayNumber + 1) / 7.0), templatePhases);
        var template = new PlanTemplate
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DefinitionJson = definition.ToJson(),
        };
        db.PlanTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return template;
    }

    // ---------------------------------------------------------------- ayudas

    private static async Task RemoveActivitiesAsync(ApplicationDbContext db, List<int> activityIds, CancellationToken ct)
    {
        if (activityIds.Count == 0)
        {
            return;
        }

        var dependencies = await db.ActivityDependencies
            .Where(d => activityIds.Contains(d.ActivityId) || activityIds.Contains(d.PredecessorId))
            .ToListAsync(ct);
        db.ActivityDependencies.RemoveRange(dependencies);
        db.WorkPlanActivities.RemoveRange(await db.WorkPlanActivities.Where(a => activityIds.Contains(a.Id)).ToListAsync(ct));
    }

    /// <summary>Rechaza dependencias circulares: ninguna predecesora puede depender, directa o indirectamente, de la actividad.</summary>
    private static async Task EnsureNoCycleAsync(ApplicationDbContext db, int activityId, List<int> newPredecessors, CancellationToken ct)
    {
        var edges = await db.ActivityDependencies.AsNoTracking()
            .Where(d => d.ActivityId != activityId)
            .Select(d => new { d.ActivityId, d.PredecessorId })
            .ToListAsync(ct);
        var predecessorsOf = edges.GroupBy(e => e.ActivityId).ToDictionary(g => g.Key, g => g.Select(e => e.PredecessorId).ToList());

        var visited = new HashSet<int>();
        var stack = new Stack<int>(newPredecessors);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == activityId)
            {
                throw new BusinessRuleException("Esa dependencia crearía un ciclo: una actividad no puede depender de otra que ya depende de ella.");
            }

            if (visited.Add(current) && predecessorsOf.TryGetValue(current, out var next))
            {
                foreach (var id in next)
                {
                    stack.Push(id);
                }
            }
        }
    }

    private static decimal Weighted(List<PlanActivityView> views)
    {
        var timed = views.Where(v => !v.Activity.IsMilestone).ToList();
        if (timed.Count == 0)
        {
            return views.Count == 0 ? 0 : Math.Round(views.Average(v => v.Progress), 1);
        }

        var total = timed.Sum(v => (decimal)Duration(v.Activity));
        return total == 0 ? 0 : Math.Round(timed.Sum(v => Duration(v.Activity) * Math.Clamp(v.Progress, 0, 100)) / total, 1);
    }

    private static int Duration(WorkPlanActivity activity) => Math.Max(1, activity.PlannedEnd.DayNumber - activity.PlannedStart.DayNumber + 1);

    private PaceResult Pace(decimal progress, DateOnly? start, DateOnly? end, int margin) =>
        PaceCalculator.Calculate(progress, start, end?.AddDays(1), clock.Today, margin);

    private PaceResult ActivityPace(WorkPlanActivity activity, decimal progress, int margin)
    {
        if (!activity.IsMilestone)
        {
            return Pace(progress, activity.PlannedStart, activity.PlannedEnd, margin);
        }

        // Un hito está "Finalizado" si ya se cumplió; "Atrasado" si su fecha pasó sin cumplirse.
        var today = clock.Today;
        if (progress >= 100)
        {
            return new PaceResult(100, 100, PaceStatus.Completed, 0, activity.PlannedStart, 0, 0);
        }

        return today > activity.PlannedStart
            ? new PaceResult(0, 100, PaceStatus.Behind, -(today.DayNumber - activity.PlannedStart.DayNumber), null, 0, 0)
            : new PaceResult(0, 0, PaceStatus.OnTime, 0, activity.PlannedStart, 0, 0);
    }
}
