using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class ProjectionService(IDbContextFactory<ApplicationDbContext> factory) : IProjectionService
{
    public async Task<List<ProjectionVersionInfo>> VersionsAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var list = await db.DesignProjections.AsNoTracking()
            .Include(p => p.Lines)
            .Where(p => p.SiteId == siteId)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);
        return list.Select(p => new ProjectionVersionInfo(p, p.Lines.Count)).ToList();
    }

    public async Task<List<ProjectionAdjustment>> AdjustmentsAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.ProjectionAdjustments.AsNoTracking()
            .Include(a => a.Material)
            .Where(a => a.SiteId == siteId)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.Id)
            .ToListAsync(ct);
    }

    public async Task<ProjectionAdjustment> AdjustAsync(int siteId, int materialId, decimal delta, string reason, DateOnly date,
        CancellationToken ct = default)
    {
        if (delta == 0)
        {
            throw new BusinessRuleException("El ajuste debe ser distinto de cero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("El motivo del ajuste es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new BusinessRuleException("El nodo no existe.");
        if (!await db.Materials.AnyAsync(m => m.Id == materialId, ct))
        {
            throw new BusinessRuleException("El material no existe.");
        }

        var adjustment = new ProjectionAdjustment
        {
            ProjectId = site.ProjectId, SiteId = siteId, MaterialId = materialId,
            Delta = delta, Reason = reason.Trim(), Date = date,
        };
        db.ProjectionAdjustments.Add(adjustment);
        await db.SaveChangesAsync(ct);
        return adjustment;
    }
}
