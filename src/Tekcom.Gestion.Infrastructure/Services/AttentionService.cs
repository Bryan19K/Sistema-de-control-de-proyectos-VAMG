using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>Junta lo que requiere atención de un proyecto con los mismos servicios que usan las pantallas.</summary>
public sealed class AttentionService(
    ISiteService sites,
    ITaskService tasks,
    IInventoryService inventory,
    IMinuteService minutes,
    IDocumentService documents,
    IMeasurementService measurements,
    ISprintService sprints,
    IDateTimeProvider clock) : IAttentionService
{
    public async Task<List<AttentionItem>> GetAsync(int projectId, CancellationToken ct = default)
    {
        var today = clock.Today;
        var items = new List<AttentionItem>();
        var siteList = await sites.ListAsync(projectId, ct);

        var late = siteList.Where(s => !s.Site.OdnFinalized && s.Pace.Status == PaceStatus.Behind).OrderBy(s => s.Site.Code).ToList();
        if (late.Count > 0)
        {
            items.Add(new AttentionItem($"{late.Count} nodo(s) atrasado(s)", Preview(late.Select(s => s.Site.Code)), "nodos",
                late.Count >= 5 ? AttentionSeverity.Critical : AttentionSeverity.Warning, late.Count));
        }

        var overdue = (await tasks.ListAsync(new TaskFilter { ProjectId = projectId, OnlyOpen = true }, ct))
            .Where(t => t.DueDate is { } due && due < today).OrderBy(t => t.DueDate).ToList();
        if (overdue.Count > 0)
        {
            items.Add(new AttentionItem($"{overdue.Count} tarea(s) vencida(s)", Preview(overdue.Select(t => t.Title)), "sprints", AttentionSeverity.Critical, overdue.Count));
        }

        var stock = (await inventory.StockAsync(projectId, null, ct)).Where(r => r.Status is StockStatus.Low or StockStatus.Negative).ToList();
        if (stock.Count > 0)
        {
            items.Add(new AttentionItem($"{stock.Count} material(es) con stock bajo", Preview(stock.Select(r => r.Material.Name)), "materiales",
                stock.Any(r => r.Status == StockStatus.Negative) ? AttentionSeverity.Critical : AttentionSeverity.Warning, stock.Count));
        }

        var pending = (await minutes.PendingAsync(projectId, null, ct)).Where(i => i.DueDate is { } due && due < today).ToList();
        if (pending.Count > 0)
        {
            items.Add(new AttentionItem($"{pending.Count} pendiente(s) de minuta vencido(s)", Preview(pending.Select(i => i.Description)), "documentos", AttentionSeverity.Warning, pending.Count));
        }

        var docs = await documents.ListAsync(new DocumentFilter { ProjectId = projectId }, ct);
        var withoutAtp = siteList.Where(s => s.Site.OdnFinalized && !docs.Any(d => d.SiteId == s.Site.Id && d.Kind == DocumentKind.Atp && d.IsSigned))
            .OrderBy(s => s.Site.Code).ToList();
        if (withoutAtp.Count > 0)
        {
            items.Add(new AttentionItem($"{withoutAtp.Count} nodo(s) finalizado(s) sin ATP firmado", Preview(withoutAtp.Select(s => s.Site.Code)), "documentos", AttentionSeverity.Warning, withoutAtp.Count));
        }

        var notAcceptable = 0;
        foreach (var s in siteList) { notAcceptable += (await measurements.SummaryAsync(s.Site.Id, ct)).NotAcceptable; }
        if (notAcceptable > 0)
        {
            items.Add(new AttentionItem($"{notAcceptable} medición(es) de potencia no aceptable(s)", "Revise las FAT medidas en los nodos.", "nodos", AttentionSeverity.Warning, notAcceptable));
        }

        var lateSprints = (await sprints.ListAsync(projectId, ct)).Where(s => s.Sprint.Status == SprintStatus.Active && s.Pace.Status == PaceStatus.Behind).ToList();
        if (lateSprints.Count > 0)
        {
            items.Add(new AttentionItem($"{lateSprints.Count} sprint(s) atrasado(s)", Preview(lateSprints.Select(s => s.Sprint.Name)), "sprints", AttentionSeverity.Warning, lateSprints.Count));
        }

        return items.OrderByDescending(i => i.Severity).ThenByDescending(i => i.Count).ToList();
    }

    private static string Preview(IEnumerable<string> names)
    {
        var list = names.ToList();
        var shown = string.Join(", ", list.Take(5));
        return list.Count > 5 ? $"{shown} y {list.Count - 5} más" : shown;
    }
}
