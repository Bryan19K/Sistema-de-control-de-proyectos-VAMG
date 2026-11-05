using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class FinanceService(IDbContextFactory<ApplicationDbContext> factory) : IFinanceService
{
    public async Task<ExpenseSummary> SummaryAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var entries = await db.ExpenseEntries.AsNoTracking().ToListAsync(ct);
        var funds = await db.ExpenseFunds.AsNoTracking().OrderBy(f => f.Kind).ThenBy(f => f.Name).ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking().ToListAsync(ct);

        var purchases = entries.Where(e => e.Kind == ExpenseKind.Purchase).ToList();
        var byCostCenter = entries
            .GroupBy(e => string.IsNullOrWhiteSpace(e.CostCenter) ? "Sin centro de costos" : e.CostCenter!.Trim())
            .Select(g => new CostCenterTotal(g.Key,
                projects.FirstOrDefault(p => string.Equals(p.Code, g.Key, StringComparison.OrdinalIgnoreCase))?.Name,
                g.Sum(e => e.Total), g.Count()))
            .OrderByDescending(c => c.Total).ToList();

        // Las compras traen tipo (TRANSPORTE, MATERIAL…); caja chica y viáticos se agrupan por su fondo.
        var byCategory = purchases.GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "OTROS" : e.Category!.Trim().ToUpperInvariant())
            .Select(g => new CategoryTotal(g.Key, g.Sum(e => e.Total)))
            .Concat(new[]
            {
                new CategoryTotal("CAJA CHICA", entries.Where(e => e.Kind == ExpenseKind.PettyCash).Sum(e => e.Total)),
                new CategoryTotal("VIÁTICOS", entries.Where(e => e.Kind == ExpenseKind.Travel).Sum(e => e.Total)),
            })
            .Where(c => c.Total > 0).OrderByDescending(c => c.Total).ToList();

        return new ExpenseSummary
        {
            PurchasesSubtotal = purchases.Sum(e => e.Subtotal ?? e.Total),
            PurchasesVat = purchases.Sum(e => e.Vat ?? 0),
            PurchasesTotal = purchases.Sum(e => e.Total),
            PettyCashTotal = entries.Where(e => e.Kind == ExpenseKind.PettyCash).Sum(e => e.Total),
            TravelTotal = entries.Where(e => e.Kind == ExpenseKind.Travel).Sum(e => e.Total),
            Entries = entries.Count,
            ByCategory = byCategory,
            ByCostCenter = byCostCenter,
            Funds = funds.Select(f =>
            {
                var own = entries.Where(e => e.FundId == f.Id).ToList();
                return new FundBalance(f, own.Sum(e => e.Total), own.Count);
            }).ToList(),
        };
    }

    public async Task<List<ExpenseEntry>> ExpensesAsync(ExpenseKind? kind, int? fundId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = db.ExpenseEntries.AsNoTracking().Include(e => e.Fund).AsQueryable();
        if (kind is { } k)
        {
            query = query.Where(e => e.Kind == k);
        }

        if (fundId is { } id)
        {
            query = query.Where(e => e.FundId == id);
        }

        var list = await query.ToListAsync(ct);
        return list.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).ToList();
    }

    public async Task<List<ContractOrder>> OrdersAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var list = await db.ContractOrders.AsNoTracking().Where(o => o.ProjectId == projectId).ToListAsync(ct);
        return list.OrderBy(o => o.Code == "FEEDER" ? 0 : 1).ThenBy(o => o.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<PriceItem>> PricesAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.PriceItems.AsNoTracking().Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.Section).ThenBy(p => p.SortOrder).ToListAsync(ct);
    }

    // ------------------------------------------------------------------ edición

    public async Task<ExpenseEntry> SaveExpenseAsync(ExpenseEntry entry, CancellationToken ct = default)
    {
        var description = (entry.Description ?? string.Empty).Trim();
        if (description.Length == 0)
        {
            throw new BusinessRuleException("Escriba el concepto o la descripción del gasto.");
        }

        if (entry.Date == default)
        {
            throw new BusinessRuleException("Elija la fecha del gasto.");
        }

        decimal total;
        decimal? subtotal = null, vat = null;
        if (entry.Kind == ExpenseKind.Purchase && entry.Subtotal is { } sub)
        {
            subtotal = sub;
            vat = entry.Vat ?? Math.Round(sub * 0.13m, 4);
            total = sub + vat.Value;
        }
        else
        {
            total = entry.Total;
        }

        if (total < 0)
        {
            throw new BusinessRuleException("El monto no puede ser negativo.");
        }

        await using var db = factory.CreateDbContext();
        ExpenseEntry target;
        if (entry.Id == 0)
        {
            target = new ExpenseEntry { SourceKey = $"MANUAL:{Guid.NewGuid():N}" };
            db.ExpenseEntries.Add(target);
        }
        else
        {
            target = await db.ExpenseEntries.FirstOrDefaultAsync(e => e.Id == entry.Id, ct)
                ?? throw new BusinessRuleException("El gasto no existe.");
        }

        var costCenter = Clean(entry.CostCenter);
        target.Kind = entry.Kind;
        target.FundId = entry.Kind == ExpenseKind.Purchase ? null : entry.FundId;
        target.Date = entry.Date;
        target.Description = description.Length > 400 ? description[..400] : description;
        target.Category = Clean(entry.Category)?.ToUpperInvariant();
        target.Supplier = Clean(entry.Supplier);
        target.InvoiceNumber = Clean(entry.InvoiceNumber);
        target.Unit = Clean(entry.Unit);
        target.Quantity = entry.Quantity;
        target.UnitCost = entry.UnitCost;
        target.Subtotal = subtotal;
        target.Vat = vat;
        target.Total = Math.Round(total, 4);
        target.Status = Clean(entry.Status);
        target.PaidBy = Clean(entry.PaidBy);
        target.CostCenter = costCenter;
        target.ProjectId = costCenter is null ? null
            : (await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Code == costCenter, ct))?.Id;
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task DeleteExpenseAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var entry = await db.ExpenseEntries.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new BusinessRuleException("El gasto no existe.");
        db.ExpenseEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ExpenseFund>> FundsAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.ExpenseFunds.AsNoTracking().OrderBy(f => f.Kind).ThenBy(f => f.Name).ToListAsync(ct);
    }

    public async Task<ExpenseFund> SaveFundAsync(ExpenseFund fund, CancellationToken ct = default)
    {
        var name = (fund.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El fondo necesita un nombre.");
        }

        if (fund.Kind == ExpenseKind.Purchase)
        {
            throw new BusinessRuleException("Los fondos son de caja chica o de viáticos.");
        }

        if (fund.AssignedAmount < 0)
        {
            throw new BusinessRuleException("El monto asignado no puede ser negativo.");
        }

        await using var db = factory.CreateDbContext();
        ExpenseFund target;
        if (fund.Id == 0)
        {
            target = new ExpenseFund { SourceKey = $"MANUAL:{Guid.NewGuid():N}" };
            db.ExpenseFunds.Add(target);
        }
        else
        {
            target = await db.ExpenseFunds.FirstOrDefaultAsync(f => f.Id == fund.Id, ct) ?? throw new BusinessRuleException("El fondo no existe.");
        }

        target.Name = name;
        target.Kind = fund.Kind;
        target.AssignedAmount = fund.AssignedAmount;
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task DeleteFundAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        if (await db.ExpenseEntries.AnyAsync(e => e.FundId == id, ct))
        {
            throw new BusinessRuleException("El fondo tiene gastos: quítelos o muévalos a otro fondo primero.");
        }

        var fund = await db.ExpenseFunds.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new BusinessRuleException("El fondo no existe.");
        db.ExpenseFunds.Remove(fund);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ContractOrder> SaveOrderAsync(ContractOrder order, CancellationToken ct = default)
    {
        var code = (order.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            throw new BusinessRuleException("Escriba el nodo o el nombre de la orden (SON01, FEEDER…).");
        }

        if (order.Subtotal < 0 || order.Vat < 0)
        {
            throw new BusinessRuleException("Los montos no pueden ser negativos.");
        }

        await using var db = factory.CreateDbContext();
        if (await db.ContractOrders.AnyAsync(o => o.ProjectId == order.ProjectId && o.Code == code && o.Id != order.Id, ct))
        {
            throw new BusinessRuleException($"Ya existe una orden para {code}.");
        }

        ContractOrder target;
        if (order.Id == 0)
        {
            target = new ContractOrder { ProjectId = order.ProjectId };
            db.ContractOrders.Add(target);
        }
        else
        {
            target = await db.ContractOrders.FirstOrDefaultAsync(o => o.Id == order.Id, ct) ?? throw new BusinessRuleException("La orden no existe.");
        }

        target.Code = code;
        target.SiteId = (await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectId == target.ProjectId && s.Code == code, ct))?.Id;
        target.OrderNumber = Clean(order.OrderNumber);
        target.HomesPassed = order.HomesPassed;
        target.Subtotal = order.Subtotal;
        target.Vat = order.Vat;
        target.Total = order.Subtotal + order.Vat;
        target.ContractorName = Clean(order.ContractorName);
        target.MaterialStatus = Clean(order.MaterialStatus);
        target.ConstructionStatus = Clean(order.ConstructionStatus);
        target.LiquidationStatus = Clean(order.LiquidationStatus);
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task DeleteOrderAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var order = await db.ContractOrders.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new BusinessRuleException("La orden no existe.");
        db.ContractOrders.Remove(order);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PriceItem> SavePriceAsync(PriceItem price, CancellationToken ct = default)
    {
        var item = (price.Item ?? string.Empty).Trim();
        if (item.Length == 0)
        {
            throw new BusinessRuleException("Escriba el rubro.");
        }

        if (price.TargetPrice < 0)
        {
            throw new BusinessRuleException("El precio no puede ser negativo.");
        }

        await using var db = factory.CreateDbContext();
        PriceItem target;
        if (price.Id == 0)
        {
            target = new PriceItem { ProjectId = price.ProjectId, SortOrder = 1000 + await db.PriceItems.CountAsync(p => p.ProjectId == price.ProjectId, ct) };
            db.PriceItems.Add(target);
        }
        else
        {
            target = await db.PriceItems.FirstOrDefaultAsync(p => p.Id == price.Id, ct) ?? throw new BusinessRuleException("El precio no existe.");
        }

        target.Section = string.IsNullOrWhiteSpace(price.Section) ? "ODN" : price.Section.Trim().ToUpperInvariant();
        target.Item = item;
        target.Type = Clean(price.Type);
        target.Unit = Clean(price.Unit);
        target.TargetPrice = price.TargetPrice;
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task DeletePriceAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var price = await db.PriceItems.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new BusinessRuleException("El precio no existe.");
        db.PriceItems.Remove(price);
        await db.SaveChangesAsync(ct);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
