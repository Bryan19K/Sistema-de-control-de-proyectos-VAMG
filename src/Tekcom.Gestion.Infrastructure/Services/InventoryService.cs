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
/// Bodega. El stock no se guarda: se calcula con los movimientos (ingresos − entregas + devoluciones
/// ± ajustes), sin contar los anulados. Una entrega y su efecto en el stock se guardan en la misma
/// transacción, y no se permite dejar el stock en negativo sin una justificación explícita.
/// </summary>
public sealed class InventoryService(
    IDbContextFactory<ApplicationDbContext> factory,
    IQuantityService quantities,
    ICurrentUserService currentUser,
    IDateTimeProvider clock) : IInventoryService
{
    public async Task<List<StockRow>> StockAsync(int projectId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var projected = await quantities.GetProjectedByMaterialAsync(projectId, ct);

        var movements = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.ProjectId == projectId && !m.IsVoided && (asOf == null || m.Date <= asOf))
            .Select(m => new { m.MaterialId, m.Type, m.Quantity, m.IsIncrease })
            .ToListAsync(ct);

        var materials = await db.Materials.AsNoTracking()
            .Where(m => m.IsActive && m.TracksStock)
            .ToListAsync(ct);

        var rows = new List<StockRow>();
        foreach (var material in materials)
        {
            var own = movements.Where(m => m.MaterialId == material.Id).ToList();
            var projectedTotal = projected.GetValueOrDefault(material.Id);
            if (own.Count == 0 && projectedTotal <= 0 && material.MinimumStock <= 0)
            {
                continue;
            }

            rows.Add(Build(material, own.Select(m => (m.Type, m.Quantity, m.IsIncrease)), projectedTotal));
        }

        return rows
            .OrderBy(r => r.Material.Category)
            .ThenBy(r => r.Material.LengthMeters ?? 0)
            .ThenBy(r => r.Material.Name)
            .ToList();
    }

    public async Task<decimal> AvailableAsync(int projectId, int materialId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await AvailableAsync(db, projectId, materialId, ct);
    }

    public async Task<InventoryMovement> ReceiptAsync(int projectId, int materialId, decimal quantity, DateOnly date,
        string? batch, string? reference, string? notes, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        await ValidateAsync(db, projectId, materialId, quantity, requireStock: true, ct);
        return await AddAsync(db, new InventoryMovement
        {
            ProjectId = projectId, Type = MovementType.Receipt, MaterialId = materialId, Quantity = quantity, Date = date,
            Batch = Clean(batch), Reference = Clean(reference), Notes = Clean(notes),
        }, ct);
    }

    public async Task<InventoryMovement> DeliveryAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date,
        int? contractorId, string? reference, string? notes, string? negativeStockJustification = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        await ValidateAsync(db, projectId, materialId, quantity, requireStock: true, ct);
        var site = await RequireOpenSiteAsync(db, projectId, siteId, ct);

        var available = await AvailableAsync(db, projectId, materialId, ct);
        var justification = Clean(negativeStockJustification);
        if (quantity > available && justification is null)
        {
            throw new BusinessRuleException(
                $"Stock insuficiente: hay {available:0.###} disponibles. Registre un ingreso o ajuste la cantidad.");
        }

        return await AddAsync(db, new InventoryMovement
        {
            ProjectId = projectId, Type = MovementType.Delivery, MaterialId = materialId, Quantity = quantity, Date = date,
            SiteId = siteId, ContractorId = contractorId ?? site.ContractorId,
            Reference = Clean(reference), Notes = Clean(notes),
            NegativeStockJustification = quantity > available ? justification : null,
        }, ct);
    }

    public async Task<InventoryMovement> ReturnAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date,
        string? reference, string? notes, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        await ValidateAsync(db, projectId, materialId, quantity, requireStock: true, ct);
        var site = await RequireOpenSiteAsync(db, projectId, siteId, ct);

        var atSite = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.SiteId == siteId && m.MaterialId == materialId && !m.IsVoided
                && (m.Type == MovementType.Delivery || m.Type == MovementType.Return))
            .Select(m => new { m.Type, m.Quantity })
            .ToListAsync(ct);
        var holding = atSite.Where(m => m.Type == MovementType.Delivery).Sum(m => m.Quantity)
            - atSite.Where(m => m.Type == MovementType.Return).Sum(m => m.Quantity);
        if (quantity > holding)
        {
            throw new BusinessRuleException(
                $"No puede devolver más de lo entregado a este nodo: queda por devolver {Math.Max(0, holding):0.###}.");
        }

        return await AddAsync(db, new InventoryMovement
        {
            ProjectId = projectId, Type = MovementType.Return, MaterialId = materialId, Quantity = quantity, Date = date,
            SiteId = siteId, ContractorId = site.ContractorId, Reference = Clean(reference), Notes = Clean(notes),
        }, ct);
    }

    public async Task<InventoryMovement> AdjustmentAsync(int projectId, int materialId, decimal quantity, bool increase,
        DateOnly date, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("El motivo del ajuste es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        await ValidateAsync(db, projectId, materialId, quantity, requireStock: true, ct);
        if (!increase)
        {
            var available = await AvailableAsync(db, projectId, materialId, ct);
            if (quantity > available)
            {
                throw new BusinessRuleException($"El ajuste dejaría el stock en negativo: hay {available:0.###} disponibles.");
            }
        }

        return await AddAsync(db, new InventoryMovement
        {
            ProjectId = projectId, Type = MovementType.Adjustment, MaterialId = materialId, Quantity = quantity,
            IsIncrease = increase, Date = date, Notes = reason.Trim(),
        }, ct);
    }

    public async Task<InventoryMovement> InstalledAsync(int projectId, int siteId, int materialId, decimal quantity, DateOnly date,
        int? contractorId, string? notes, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        await ValidateAsync(db, projectId, materialId, quantity, requireStock: false, ct);
        var site = await RequireOpenSiteAsync(db, projectId, siteId, ct);

        return await AddAsync(db, new InventoryMovement
        {
            ProjectId = projectId, Type = MovementType.Installed, MaterialId = materialId, Quantity = quantity, Date = date,
            SiteId = siteId, ContractorId = contractorId ?? site.ContractorId, Notes = Clean(notes),
        }, ct);
    }

    public async Task<decimal> SetInstalledTotalAsync(int projectId, int siteId, int materialId, decimal newTotal, DateOnly date,
        string? reason, CancellationToken ct = default)
    {
        if (newTotal < 0)
        {
            throw new BusinessRuleException("Lo instalado no puede ser negativo.");
        }

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var site = await RequireOpenSiteAsync(db, projectId, siteId, ct);
        var current = await db.InventoryMovements
            .Where(m => m.ProjectId == projectId && m.SiteId == siteId && m.MaterialId == materialId && !m.IsVoided && m.Type == MovementType.Installed)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .ToListAsync(ct);
        var total = current.Sum(m => m.Quantity);
        var delta = newTotal - total;
        if (delta == 0)
        {
            return total;
        }

        if (delta > 0)
        {
            await ValidateAsync(db, projectId, materialId, delta, requireStock: false, ct);
            db.InventoryMovements.Add(new InventoryMovement
            {
                ProjectId = projectId, Type = MovementType.Installed, MaterialId = materialId, Quantity = delta, Date = date,
                SiteId = siteId, ContractorId = site.ContractorId, Notes = Clean(reason) ?? "Ajuste del total instalado",
            });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new BusinessRuleException("Para bajar lo instalado escriba el motivo.");
            }

            var toRemove = -delta;
            foreach (var movement in current)
            {
                if (toRemove <= 0)
                {
                    break;
                }

                var note = $"Corrección del total instalado: {reason.Trim()}";
                if (movement.Quantity <= toRemove)
                {
                    toRemove -= movement.Quantity;
                    movement.IsVoided = true;
                    movement.VoidReason = note;
                    movement.VoidedAt = DateTimeOffset.UtcNow;
                    movement.VoidedBy = "Corrección de instalado";
                }
                else
                {
                    // Se anula el registro completo y se vuelve a anotar lo que sí quedó instalado.
                    var keep = movement.Quantity - toRemove;
                    movement.IsVoided = true;
                    movement.VoidReason = note;
                    movement.VoidedAt = DateTimeOffset.UtcNow;
                    movement.VoidedBy = "Corrección de instalado";
                    db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProjectId = projectId, Type = MovementType.Installed, MaterialId = materialId, Quantity = keep, Date = movement.Date,
                        SiteId = siteId, ContractorId = movement.ContractorId, Notes = $"Queda tras corregir: {reason.Trim()}",
                    });
                    toRemove = 0;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return newTotal;
    }

    public async Task<decimal> SetDeliveredTotalAsync(int projectId, int siteId, int materialId, decimal newTotal, DateOnly date, string? reason,
        CancellationToken ct = default)
    {
        if (newTotal < 0)
        {
            throw new BusinessRuleException("Lo entregado no puede ser negativo.");
        }

        List<InventoryMovement> current;
        await using (var read = factory.CreateDbContext())
        {
            current = await read.InventoryMovements.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.SiteId == siteId && m.MaterialId == materialId && !m.IsVoided && m.Type == MovementType.Delivery)
                .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToListAsync(ct);
        }

        var total = current.Sum(m => m.Quantity);
        var delta = newTotal - total;
        if (delta == 0)
        {
            return total;
        }

        if (delta > 0)
        {
            // Con motivo escrito se permite dejar el stock en negativo (queda registrado como justificación).
            await DeliveryAsync(projectId, siteId, materialId, delta, date, current.FirstOrDefault()?.ContractorId, null,
                string.IsNullOrWhiteSpace(reason) ? "Ajuste del total entregado" : reason.Trim(),
                string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), ct);
            return newTotal;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("Para bajar lo entregado escriba el motivo.");
        }

        var toRemove = -delta;
        foreach (var movement in current)
        {
            if (toRemove <= 0)
            {
                break;
            }

            if (movement.Quantity <= toRemove)
            {
                await VoidAsync(movement.Id, $"Corrección del total entregado: {reason.Trim()}", ct);
                toRemove -= movement.Quantity;
            }
            else
            {
                var keep = movement.Quantity - toRemove;
                await VoidAsync(movement.Id, $"Corrección del total entregado: {reason.Trim()}", ct);
                await DeliveryAsync(projectId, siteId, materialId, keep, movement.Date, movement.ContractorId, movement.Reference,
                    $"Queda tras corregir: {reason.Trim()}", null, ct);
                toRemove = 0;
            }
        }

        return newTotal;
    }

    public async Task<InventoryMovement> UpdateMovementAsync(int movementId, decimal quantity, DateOnly date, int? contractorId, string? reference,
        string? batch, string? notes, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("La cantidad debe ser mayor que cero.");
        }

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var movement = await db.InventoryMovements.Include(m => m.Material).FirstOrDefaultAsync(m => m.Id == movementId, ct)
            ?? throw new BusinessRuleException("El movimiento no existe.");
        if (movement.IsVoided)
        {
            throw new BusinessRuleException("El movimiento está anulado: no se puede editar.");
        }

        if (movement.SiteId is { } siteId
            && await db.Liquidations.AnyAsync(l => l.SiteId == siteId && l.Status == LiquidationStatus.Closed, ct))
        {
            throw new BusinessRuleException("El nodo está liquidado: reabra la liquidación para editar movimientos.");
        }

        decimal Effect(decimal q) => movement.Type switch
        {
            MovementType.Receipt or MovementType.Return => q,
            MovementType.Delivery => -q,
            MovementType.Adjustment => movement.IsIncrease ? q : -q,
            _ => 0,
        };

        var change = Effect(quantity) - Effect(movement.Quantity);
        if (change < 0 && movement.Material!.TracksStock)
        {
            var available = await AvailableAsync(db, movement.ProjectId, movement.MaterialId, ct);
            if (available + change < 0)
            {
                throw new BusinessRuleException($"No se puede: el stock quedaría en negativo (hay {available:0.###} disponibles).");
            }
        }

        movement.Quantity = quantity;
        movement.Date = date;
        if (movement.Type is MovementType.Delivery or MovementType.Installed or MovementType.Return)
        {
            movement.ContractorId = contractorId;
        }

        movement.Reference = Clean(reference);
        movement.Batch = Clean(batch);
        movement.Notes = Clean(notes);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return movement;
    }

    public async Task VoidAsync(int movementId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("El motivo de la anulación es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var movement = await db.InventoryMovements.Include(m => m.Material).FirstOrDefaultAsync(m => m.Id == movementId, ct)
            ?? throw new BusinessRuleException("El movimiento no existe.");
        if (movement.IsVoided)
        {
            throw new BusinessRuleException("El movimiento ya está anulado.");
        }

        if (movement.SiteId is { } siteId
            && await db.Liquidations.AnyAsync(l => l.SiteId == siteId && l.Status == LiquidationStatus.Closed, ct))
        {
            throw new BusinessRuleException("El nodo está liquidado: reabra la liquidación para anular movimientos.");
        }

        var reducesStock = movement.Material!.TracksStock && movement.Type is MovementType.Receipt or MovementType.Return
            || movement.Material.TracksStock && movement.Type == MovementType.Adjustment && movement.IsIncrease;
        if (reducesStock)
        {
            var available = await AvailableAsync(db, movement.ProjectId, movement.MaterialId, ct);
            if (movement.Quantity > available)
            {
                throw new BusinessRuleException(
                    $"No se puede anular: el stock quedaría en negativo (hay {available:0.###} disponibles).");
            }
        }

        movement.IsVoided = true;
        movement.VoidReason = reason.Trim();
        movement.VoidedAt = clock.UtcNow;
        movement.VoidedBy = currentUser.UserName ?? currentUser.UserId;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<List<InventoryMovement>> MovementsAsync(MovementFilter filter, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.InventoryMovements.AsNoTracking()
            .Include(m => m.Material).Include(m => m.Site).Include(m => m.Contractor)
            .Where(m => m.ProjectId == filter.ProjectId);

        if (filter.Type is { } type) { query = query.Where(m => m.Type == type); }
        if (filter.MaterialId is { } materialId) { query = query.Where(m => m.MaterialId == materialId); }
        if (filter.SiteId is { } siteId) { query = query.Where(m => m.SiteId == siteId); }
        if (filter.From is { } from) { query = query.Where(m => m.Date >= from); }
        if (filter.To is { } to) { query = query.Where(m => m.Date <= to); }
        if (!filter.IncludeVoided) { query = query.Where(m => !m.IsVoided); }

        return await query.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).Take(filter.Take).ToListAsync(ct);
    }

    // ---------------------------------------------------------------- ayudas

    private static StockRow Build(Material material, IEnumerable<(MovementType Type, decimal Quantity, bool IsIncrease)> movements, decimal projectedTotal)
    {
        var list = movements.ToList();
        return new StockRow
        {
            Material = material,
            Received = list.Where(m => m.Type == MovementType.Receipt).Sum(m => m.Quantity),
            Delivered = list.Where(m => m.Type == MovementType.Delivery).Sum(m => m.Quantity),
            Returned = list.Where(m => m.Type == MovementType.Return).Sum(m => m.Quantity),
            Installed = list.Where(m => m.Type == MovementType.Installed).Sum(m => m.Quantity),
            AdjustmentNet = list.Where(m => m.Type == MovementType.Adjustment).Sum(m => m.IsIncrease ? m.Quantity : -m.Quantity),
            ProjectedTotal = projectedTotal,
        };
    }

    private static async Task<decimal> AvailableAsync(ApplicationDbContext db, int projectId, int materialId, CancellationToken ct)
    {
        var movements = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.MaterialId == materialId && !m.IsVoided && m.Type != MovementType.Installed)
            .Select(m => new { m.Type, m.Quantity, m.IsIncrease })
            .ToListAsync(ct);

        return movements.Sum(m => m.Type switch
        {
            MovementType.Receipt or MovementType.Return => m.Quantity,
            MovementType.Delivery => -m.Quantity,
            MovementType.Adjustment => m.IsIncrease ? m.Quantity : -m.Quantity,
            _ => 0,
        });
    }

    private static async Task ValidateAsync(ApplicationDbContext db, int projectId, int materialId, decimal quantity,
        bool requireStock, CancellationToken ct)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("La cantidad debe ser mayor que cero.");
        }

        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }

        var material = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == materialId, ct)
            ?? throw new BusinessRuleException("El material no existe.");
        if (requireStock && !material.TracksStock)
        {
            throw new BusinessRuleException($"'{material.Name}' no pasa por bodega.");
        }
    }

    private static async Task<Site> RequireOpenSiteAsync(ApplicationDbContext db, int projectId, int siteId, CancellationToken ct)
    {
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == siteId && s.ProjectId == projectId, ct)
            ?? throw new BusinessRuleException("Seleccione un nodo del proyecto.");
        if (await db.Liquidations.AnyAsync(l => l.SiteId == siteId && l.Status == LiquidationStatus.Closed, ct))
        {
            throw new BusinessRuleException($"El nodo {site.Code} está liquidado: reabra la liquidación para registrar movimientos.");
        }

        return site;
    }

    private static async Task<InventoryMovement> AddAsync(ApplicationDbContext db, InventoryMovement movement, CancellationToken ct)
    {
        db.InventoryMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        return movement;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
