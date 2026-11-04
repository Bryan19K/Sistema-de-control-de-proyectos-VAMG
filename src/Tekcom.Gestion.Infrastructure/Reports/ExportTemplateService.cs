using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Reports;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Reports;

public sealed partial class ExportTemplateService(IDbContextFactory<ApplicationDbContext> factory) : IExportTemplateService
{
    public async Task<List<ExportTemplate>> ListAsync(string? reportKey = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var list = await db.ExportTemplates.AsNoTracking()
            .OrderByDescending(t => t.IsSystem).ThenBy(t => t.Name)
            .ToListAsync(ct);
        return reportKey is null ? list : list.Where(t => t.ReportKey is null || t.ReportKey == reportKey).ToList();
    }

    public async Task<ExportTemplate> GetAsync(int? id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var template = id is null ? null : await db.ExportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return template
            ?? await db.ExportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.IsSystem, ct)
            ?? new ExportTemplate { Name = "Estándar TEK-COM", IsSystem = true };
    }

    public async Task<ExportTemplate> SaveAsync(ExportTemplate template, CancellationToken ct = default)
    {
        var name = (template.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("El nombre de la plantilla es obligatorio.");
        }

        if (!HexColor().IsMatch(template.HeaderColor ?? string.Empty) || !HexColor().IsMatch(template.AccentColor ?? string.Empty))
        {
            throw new BusinessRuleException("Los colores deben tener el formato #RRGGBB (por ejemplo #12304f).");
        }

        await using var db = factory.CreateDbContext();
        if (await db.ExportTemplates.AnyAsync(t => t.Id != template.Id && t.Name == name, ct))
        {
            throw new BusinessRuleException($"Ya existe una plantilla llamada '{name}'.");
        }

        var entity = template.Id == 0
            ? db.ExportTemplates.Add(new ExportTemplate()).Entity
            : await db.ExportTemplates.FirstAsync(t => t.Id == template.Id, ct);
        if (entity.IsSystem)
        {
            throw new BusinessRuleException("La plantilla estándar no se modifica: cree una copia con su diseño.");
        }

        entity.Name = name;
        entity.ReportKey = string.IsNullOrWhiteSpace(template.ReportKey) ? null : template.ReportKey;
        entity.HeaderColor = template.HeaderColor!;
        entity.AccentColor = template.AccentColor!;
        entity.Landscape = template.Landscape;
        entity.ZebraRows = template.ZebraRows;
        entity.FooterNote = string.IsNullOrWhiteSpace(template.FooterNote) ? null : template.FooterNote.Trim();
        entity.HiddenColumns = string.IsNullOrWhiteSpace(template.HiddenColumns) ? null : template.HiddenColumns.Trim();
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var template = await db.ExportTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return;
        }

        if (template.IsSystem)
        {
            throw new BusinessRuleException("La plantilla estándar no se puede eliminar.");
        }

        db.ExportTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
