using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class FeederService(IDbContextFactory<ApplicationDbContext> factory) : IFeederService
{
    public async Task<List<FeederProgress>> GetAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var lines = await db.FeederLines.AsNoTracking()
            .Include(l => l.Entries)
            .Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.Kind)
            .ToListAsync(ct);

        return lines.Select(l => new FeederProgress(l, l.Entries.Where(e => !e.IsDeleted).Sum(e => e.Quantity))).ToList();
    }

    public async Task SavePlannedAsync(int projectId, FeederItemKind kind, decimal planned, CancellationToken ct = default)
    {
        if (planned < 0)
        {
            throw new BusinessRuleException("La cantidad proyectada no puede ser negativa.");
        }

        await using var db = factory.CreateDbContext();
        var line = await db.FeederLines.FirstOrDefaultAsync(l => l.ProjectId == projectId && l.Kind == kind, ct);
        if (line is null)
        {
            db.FeederLines.Add(new FeederLine { ProjectId = projectId, Kind = kind, PlannedQuantity = planned });
        }
        else
        {
            line.PlannedQuantity = planned;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AddEntryAsync(int projectId, FeederItemKind kind, DateOnly date, decimal quantity, string? notes, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("La cantidad instalada debe ser mayor que cero.");
        }

        await using var db = factory.CreateDbContext();
        var line = await db.FeederLines.FirstOrDefaultAsync(l => l.ProjectId == projectId && l.Kind == kind, ct)
            ?? throw new BusinessRuleException("Primero defina la cantidad proyectada de este tipo de feeder.");

        db.FeederEntries.Add(new FeederEntry
        {
            FeederLineId = line.Id,
            Date = date,
            Quantity = quantity,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<FeederEntry>> EntriesAsync(int projectId, FeederItemKind kind, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.FeederEntries.AsNoTracking()
            .Where(e => e.FeederLine!.ProjectId == projectId && e.FeederLine.Kind == kind)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).ToListAsync(ct);
    }

    public async Task UpdateEntryAsync(int entryId, DateOnly date, decimal quantity, string? notes, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("La cantidad instalada debe ser mayor que cero.");
        }

        await using var db = factory.CreateDbContext();
        var entry = await db.FeederEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct) ?? throw new BusinessRuleException("El registro no existe.");
        entry.Date = date;
        entry.Quantity = quantity;
        entry.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveEntryAsync(int entryId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var entry = await db.FeederEntries.FirstOrDefaultAsync(e => e.Id == entryId, ct) ?? throw new BusinessRuleException("El registro no existe.");
        db.FeederEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }
}
