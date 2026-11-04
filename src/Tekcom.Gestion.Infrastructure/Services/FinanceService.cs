using Microsoft.EntityFrameworkCore;
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
}
