using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class MinuteService(IDbContextFactory<ApplicationDbContext> factory, IDateTimeProvider clock) : IMinuteService
{
    public async Task<List<Minute>> ListAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var minutes = await db.Minutes.AsNoTracking().Include(m => m.Items).ThenInclude(i => i.Site)
            .Where(m => m.ProjectId == projectId).AsSplitQuery().ToListAsync(ct);
        return minutes.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToList();
    }

    public async Task<Minute?> GetAsync(int minuteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Minutes.AsNoTracking().Include(m => m.Items).ThenInclude(i => i.Site)
            .AsSplitQuery().FirstOrDefaultAsync(m => m.Id == minuteId, ct);
    }

    public async Task<Minute> SaveAsync(Minute minute, CancellationToken ct = default)
    {
        var title = (minute.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new BusinessRuleException("El título de la minuta es obligatorio.");
        }

        if (minute.Items.Any(i => string.IsNullOrWhiteSpace(i.Description)))
        {
            throw new BusinessRuleException("Todos los pendientes necesitan una descripción.");
        }

        await using var db = factory.CreateDbContext();
        if (!await db.Projects.AnyAsync(p => p.Id == minute.ProjectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }

        var siteIds = minute.Items.Where(i => i.SiteId is not null).Select(i => i.SiteId!.Value).Distinct().ToList();
        if (siteIds.Count > 0 && await db.Sites.CountAsync(s => s.ProjectId == minute.ProjectId && siteIds.Contains(s.Id), ct) != siteIds.Count)
        {
            throw new BusinessRuleException("Un nodo de los pendientes no pertenece a este proyecto.");
        }

        var entity = minute.Id == 0
            ? db.Minutes.Add(new Minute { ProjectId = minute.ProjectId }).Entity
            : await db.Minutes.Include(m => m.Items).FirstOrDefaultAsync(m => m.Id == minute.Id, ct)
              ?? throw new BusinessRuleException("La minuta no existe.");

        entity.Date = minute.Date;
        entity.Title = title;
        entity.Attendees = Clean(minute.Attendees);
        entity.Summary = Clean(minute.Summary);

        var incomingIds = minute.Items.Where(i => i.Id != 0).Select(i => i.Id).ToHashSet();
        foreach (var removed in entity.Items.Where(i => !incomingIds.Contains(i.Id)).ToList())
        {
            db.MinuteItems.Remove(removed);
        }

        foreach (var incoming in minute.Items)
        {
            var item = incoming.Id == 0
                ? db.MinuteItems.Add(new MinuteItem { Minute = entity }).Entity
                : entity.Items.First(i => i.Id == incoming.Id);

            item.Description = incoming.Description.Trim();
            item.ResponsibleUserId = string.IsNullOrWhiteSpace(incoming.ResponsibleUserId) ? null : incoming.ResponsibleUserId;
            item.SiteId = incoming.SiteId;
            item.DueDate = incoming.DueDate;
            if (item.IsDone != incoming.IsDone)
            {
                item.IsDone = incoming.IsDone;
                item.DoneOn = incoming.IsDone ? clock.Today : null;
            }
        }

        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task SetItemDoneAsync(int itemId, bool done, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var item = await db.MinuteItems.FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new BusinessRuleException("El pendiente no existe.");
        item.IsDone = done;
        item.DoneOn = done ? clock.Today : null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<MinuteItem>> PendingAsync(int projectId, int? siteId = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.MinuteItems.AsNoTracking().Include(i => i.Minute).Include(i => i.Site)
            .Where(i => !i.IsDone && i.Minute!.ProjectId == projectId);
        if (siteId is { } site)
        {
            query = query.Where(i => i.SiteId == site);
        }

        var items = await query.ToListAsync(ct);
        return items.OrderBy(i => i.DueDate ?? DateOnly.MaxValue).ThenBy(i => i.Id).ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
