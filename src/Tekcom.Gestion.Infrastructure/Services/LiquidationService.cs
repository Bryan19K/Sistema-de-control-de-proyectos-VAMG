using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>
/// Liquidación por nodo: sobrante = entregado − instalado, pendiente = sobrante − devuelto,
/// variación = instalado − proyectado. No permite cerrar con pendientes sin justificar.
/// </summary>
public sealed class LiquidationService(
    IDbContextFactory<ApplicationDbContext> factory,
    IQuantityService quantities,
    ICurrentUserService currentUser,
    IDateTimeProvider clock) : ILiquidationService
{
    public async Task<LiquidationView> GetAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var site = await db.Sites.AsNoTracking().Include(s => s.Contractor).FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new BusinessRuleException("El nodo no existe.");

        var liquidation = await db.Liquidations.AsNoTracking().Include(l => l.Justifications)
            .FirstOrDefaultAsync(l => l.SiteId == siteId, ct);
        if (liquidation is null)
        {
            var created = new Liquidation { ProjectId = site.ProjectId, SiteId = siteId };
            db.Liquidations.Add(created);
            await db.SaveChangesAsync(ct);
            liquidation = created;
        }

        var materials = (await quantities.GetSiteMaterialsAsync(siteId, ct)).Where(q => q.Material.TracksStock).ToList();
        var justifications = await db.LiquidationJustifications.AsNoTracking()
            .Include(j => j.Material)
            .Where(j => j.LiquidationId == liquidation.Id)
            .OrderBy(j => j.Id)
            .ToListAsync(ct);

        var rows = materials.Select(q => new LiquidationRow
        {
            Quantity = q,
            Justified = justifications.Where(j => j.MaterialId == q.Material.Id).Sum(j => j.Quantity),
        }).ToList();

        return new LiquidationView { Liquidation = liquidation, Site = site, Rows = rows, Justifications = justifications };
    }

    public async Task AddJustificationAsync(int siteId, int materialId, decimal quantity, string reason, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("La cantidad a justificar debe ser mayor que cero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("El motivo de la justificación es obligatorio.");
        }

        var view = await GetAsync(siteId, ct);
        if (view.IsClosed)
        {
            throw new BusinessRuleException("La liquidación está cerrada.");
        }

        var row = view.Rows.FirstOrDefault(r => r.Quantity.Material.Id == materialId)
            ?? throw new BusinessRuleException("Ese material no tiene movimientos en este nodo.");
        if (quantity > row.Unresolved)
        {
            throw new BusinessRuleException(
                $"Solo hay {row.Unresolved:0.###} pendientes por justificar de {row.Quantity.Material.Name}.");
        }

        await using var db = factory.CreateDbContext();
        db.LiquidationJustifications.Add(new LiquidationJustification
        {
            LiquidationId = view.Liquidation.Id, MaterialId = materialId, Quantity = quantity, Reason = reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveJustificationAsync(int justificationId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var justification = await db.LiquidationJustifications.Include(j => j.Liquidation)
            .FirstOrDefaultAsync(j => j.Id == justificationId, ct);
        if (justification is null)
        {
            return;
        }

        if (justification.Liquidation!.Status == LiquidationStatus.Closed)
        {
            throw new BusinessRuleException("La liquidación está cerrada.");
        }

        db.LiquidationJustifications.Remove(justification);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveObservationsAsync(int siteId, string? observations, CancellationToken ct = default)
    {
        var view = await GetAsync(siteId, ct);
        await using var db = factory.CreateDbContext();
        var liquidation = await db.Liquidations.FirstAsync(l => l.Id == view.Liquidation.Id, ct);
        liquidation.Observations = string.IsNullOrWhiteSpace(observations) ? null : observations.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(int siteId, CancellationToken ct = default)
    {
        var view = await GetAsync(siteId, ct);
        if (view.IsClosed)
        {
            throw new BusinessRuleException("La liquidación ya está cerrada.");
        }

        if (!view.CanClose)
        {
            throw new BusinessRuleException(view.BlockingMessage ?? "No se puede cerrar la liquidación.");
        }

        await using var db = factory.CreateDbContext();
        var liquidation = await db.Liquidations.FirstAsync(l => l.Id == view.Liquidation.Id, ct);
        liquidation.Status = LiquidationStatus.Closed;
        liquidation.ClosedOn = clock.Today;
        liquidation.ClosedBy = currentUser.UserName ?? currentUser.UserId;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var liquidation = await db.Liquidations.FirstOrDefaultAsync(l => l.SiteId == siteId, ct)
            ?? throw new BusinessRuleException("El nodo no tiene liquidación.");
        liquidation.Status = LiquidationStatus.Open;
        liquidation.ClosedOn = null;
        liquidation.ClosedBy = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsClosedAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Liquidations.AnyAsync(l => l.SiteId == siteId && l.Status == LiquidationStatus.Closed, ct);
    }
}
