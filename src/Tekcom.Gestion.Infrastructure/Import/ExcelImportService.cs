using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Infrastructure.Import;

/// <summary>
/// Importa el Excel de control. Reglas:
/// - Las filas con error no se importan a la fuerza: quedan listadas en el reporte.
/// - Importar de nuevo no duplica: los nodos se actualizan solo en lo que está vacío, la proyección
///   crea una versión nueva solo si cambió y lo instalado agrega únicamente la diferencia.
/// - Al terminar se validan los totales contra la fila TOTAL del Excel.
/// </summary>
public sealed class ExcelImportService(
    IDbContextFactory<ApplicationDbContext> factory,
    IQuantityService quantities,
    IDateTimeProvider clock) : IExcelImportService
{
    public async Task<ImportAnalysis> AnalyzeAsync(Stream file, string fileName, CancellationToken ct = default)
    {
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        memory.Position = 0;
        return new ExcelWorkbookParser().Parse(memory, fileName);
    }

    public async Task<ImportReport> ApplyAsync(ImportAnalysis analysis, ImportTarget target, CancellationToken ct = default)
    {
        var workbook = analysis.Workbook;
        var nodes = workbook.Nodes.Where(n => !n.HasErrors).ToList();
        if (nodes.Count == 0 && workbook.Feeder is null)
        {
            throw new BusinessRuleException("No hay datos válidos para importar. Revise los errores del reporte.");
        }

        var report = new ImportReport { FileName = analysis.Report.FileName };
        report.Issues.AddRange(analysis.Report.Issues);
        report.NodeCodes.AddRange(analysis.Report.NodeCodes);
        report.SuggestedProjectCode = analysis.Report.SuggestedProjectCode;
        report.SuggestedProjectName = analysis.Report.SuggestedProjectName;
        report.Counts.RowsSkipped = analysis.Report.Counts.RowsSkipped + workbook.Nodes.Count(n => n.HasErrors);

        var today = clock.Today;
        var reference = $"Importación Excel: {analysis.Report.FileName}";

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CatalogSeeder.EnsureAllAsync(db, ct);

        var project = await ResolveProjectAsync(db, target, nodes, ct);

        // ---------------- Fase A: datos de referencia y nodos
        var index = await MaterialIndex.LoadAsync(db, ct);
        var contractors = await db.Contractors.ToListAsync(ct);
        var supervisors = await db.Supervisors.ToListAsync(ct);
        var sites = await db.Sites.Where(s => s.ProjectId == project.Id).ToListAsync(ct);

        foreach (var node in nodes)
        {
            foreach (var quantity in node.Quantities)
            {
                index.Resolve(quantity);
            }
        }

        if (workbook.Feeder is { } feederData)
        {
            foreach (var quantity in feederData.Materials)
            {
                index.Resolve(quantity);
            }
        }

        var siteByNode = new Dictionary<string, Site>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var contractor = FindOrCreate(contractors, node.Brigade, node.Phone, report,
                name => new Contractor { Name = name, Phone = node.Phone }, db.Contractors, c => c.Name,
                (c, phone) => { if (string.IsNullOrWhiteSpace(c.Phone)) c.Phone = phone; }, c => c.Phone, "contratista");
            var supervisor = FindOrCreate(supervisors, node.Supervisor, null, report,
                name => new Supervisor { Name = name }, db.Supervisors, s => s.Name,
                (_, _) => { }, _ => null, "supervisor");

            var site = sites.FirstOrDefault(s => string.Equals(s.Code, node.Code, StringComparison.OrdinalIgnoreCase));
            if (site is null)
            {
                site = new Site
                {
                    ProjectId = project.Id,
                    Code = node.Code,
                    Zone = node.Zone,
                    Latitude = node.Latitude,
                    Longitude = node.Longitude,
                    Contractor = contractor,
                    Supervisor = supervisor,
                    PlannedStart = node.Start,
                    PlannedEnd = node.End,
                    Notes = node.Notes,
                    OdnFinalized = node.DataStatus == "FINALIZADO",
                    OdnFinalizedOn = node.DataStatus == "FINALIZADO" ? node.End : null,
                    LogsUpToDate = node.DataStatus is "ACTUALIZADO" or "FINALIZADO",
                };
                db.Sites.Add(site);
                sites.Add(site);
                report.Counts.SitesCreated++;
            }
            else
            {
                // Un nodo existente solo se completa en lo que está vacío: no se pisa lo que el usuario editó.
                site.Zone ??= node.Zone;
                site.Latitude ??= node.Latitude;
                site.Longitude ??= node.Longitude;
                site.Contractor ??= contractor;
                site.Supervisor ??= supervisor;
                site.PlannedStart ??= node.Start;
                site.PlannedEnd ??= node.End;
                site.Notes ??= node.Notes;
                if (node.DataStatus == "FINALIZADO" && !site.OdnFinalized)
                {
                    site.OdnFinalized = true;
                    site.OdnFinalizedOn ??= node.End;
                }

                if (node.DataStatus is "ACTUALIZADO" or "FINALIZADO")
                {
                    site.LogsUpToDate = true;
                }

                report.Counts.SitesUpdated++;
            }

            siteByNode[node.Code] = site;
        }

        await db.SaveChangesAsync(ct);

        // ---------------- Fase B: etapas, proyección e instalado
        var siteIds = siteByNode.Values.Select(s => s.Id).ToList();

        var latestProjections = (await db.DesignProjections
                .Where(p => p.ProjectId == project.Id)
                .Include(p => p.Lines)
                .ToListAsync(ct))
            .GroupBy(p => p.SiteId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Version).First());

        var installedNow = (await db.InventoryMovements
                .Where(m => m.ProjectId == project.Id && m.Type == MovementType.Installed && !m.IsVoided)
                .Select(m => new { m.SiteId, m.MaterialId, m.Quantity })
                .ToListAsync(ct))
            .GroupBy(m => (m.SiteId, m.MaterialId))
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        foreach (var node in nodes)
        {
            var site = siteByNode[node.Code];
            await SiteService.EnsureStagesAsync(db, site, project.ProjectTypeId, ct);
            await ApplyStagesAsync(db, site, node, ct);

            // Proyección: una versión nueva solo si cambió.
            var projected = new Dictionary<int, decimal>();
            foreach (var q in node.Quantities.Where(q => q.Projected is > 0))
            {
                var material = index.Resolve(q);
                projected[material.Id] = projected.GetValueOrDefault(material.Id) + q.Projected!.Value;
            }

            if (projected.Count > 0)
            {
                latestProjections.TryGetValue(site.Id, out var latest);
                var same = latest is not null
                    && latest.Lines.Count == projected.Count
                    && latest.Lines.All(l => projected.TryGetValue(l.MaterialId, out var qty) && qty == l.Quantity);

                if (!same)
                {
                    var projection = new DesignProjection
                    {
                        ProjectId = project.Id,
                        SiteId = site.Id,
                        Version = (latest?.Version ?? 0) + 1,
                        Date = today,
                        Source = analysis.Report.FileName,
                        Notes = "Importada del Excel de control.",
                    };
                    foreach (var (materialId, qty) in projected)
                    {
                        projection.Lines.Add(new DesignProjectionLine { MaterialId = materialId, Quantity = qty });
                    }

                    db.DesignProjections.Add(projection);
                    report.Counts.ProjectionVersionsCreated++;
                }
            }

            // Instalado: solo se agrega la diferencia contra lo que el sistema ya tiene.
            foreach (var q in node.Quantities.Where(q => q.Installed is > 0))
            {
                var material = index.Resolve(q);
                var current = installedNow.GetValueOrDefault((site.Id, material.Id));
                var delta = q.Installed!.Value - current;
                if (delta > 0)
                {
                    db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProjectId = project.Id,
                        Type = MovementType.Installed,
                        MaterialId = material.Id,
                        Quantity = delta,
                        SiteId = site.Id,
                        ContractorId = site.ContractorId,
                        Date = today,
                        Reference = reference,
                        Notes = "Saldo cargado desde el Excel (acumulado a la fecha de importación).",
                        IsOpeningBalance = true,
                    });
                    report.Counts.InstalledMovementsCreated++;
                }
                else if (delta < 0)
                {
                    report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "AVANCE ODN", node.Row, q.MaterialName,
                        $"{node.Code}: el sistema ya tiene más instalado ({current:0.###}) que el Excel ({q.Installed:0.###}). No se modifica."));
                }
            }
        }

        if (workbook.Feeder is { } feeder)
        {
            await ApplyFeederAsync(db, project, feeder, index, contractors, report, reference, today, ct);
        }

        await db.SaveChangesAsync(ct);
        report.Counts.MaterialsCreated = index.Created;
        await transaction.CommitAsync(ct);

        await BuildSystemChecksAsync(report, workbook, project.Id, siteIds, nodes.Count, ct);
        report.Applied = true;
        report.ProjectId = project.Id;
        return report;
    }

    // ------------------------------------------------------------------ proyecto

    private static async Task<Project> ResolveProjectAsync(ApplicationDbContext db, ImportTarget target,
        List<ParsedNode> nodes, CancellationToken ct)
    {
        if (target.ProjectId is { } id)
        {
            return await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new BusinessRuleException("El proyecto seleccionado no existe.");
        }

        var code = (target.NewProjectCode ?? string.Empty).Trim();
        var name = (target.NewProjectName ?? string.Empty).Trim();
        if (code.Length == 0 || name.Length == 0)
        {
            throw new BusinessRuleException("Indique el código y el nombre del proyecto nuevo.");
        }

        var existing = await db.Projects.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (existing is not null)
        {
            return existing;
        }

        var ftth = await db.ProjectTypes.FirstAsync(t => t.Code == CatalogSeeder.FtthCode, ct);
        var operatorName = nodes.Select(n => n.Operator).Where(o => !string.IsNullOrWhiteSpace(o))
            .GroupBy(o => o!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

        Client? client = null;
        if (operatorName is not null)
        {
            var key = NameNormalizer.Key(operatorName);
            client = (await db.Clients.ToListAsync(ct)).FirstOrDefault(c => NameNormalizer.Key(c.Name) == key);
            if (client is null)
            {
                client = new Client { Name = ToTitle(operatorName) };
                db.Clients.Add(client);
            }
        }

        var project = new Project
        {
            Code = code,
            Name = name,
            Client = client,
            ProjectTypeId = ftth.Id,
            Status = ProjectStatus.Active,
            PlannedStart = nodes.Where(n => n.Start is not null).Select(n => n.Start).Min(),
            PlannedEnd = nodes.Where(n => n.End is not null).Select(n => n.End).Max(),
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    private static string ToTitle(string text) =>
        text == text.ToUpperInvariant()
            ? CultureInfo.GetCultureInfo("es-SV").TextInfo.ToTitleCase(text.ToLowerInvariant())
            : text;

    private static T? FindOrCreate<T>(List<T> list, string? name, string? phone, ImportReport report,
        Func<string, T> create, DbSet<T> set, Func<T, string> nameOf, Action<T, string?> fillPhone,
        Func<T, string?> phoneOf, string kind) where T : class
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var key = NameNormalizer.Key(name);
        var found = list.FirstOrDefault(x => NameNormalizer.Key(nameOf(x)) == key);
        if (found is null)
        {
            found = create(name.Trim());
            set.Add(found);
            list.Add(found);
            if (kind == "contratista")
            {
                report.Counts.ContractorsCreated++;
            }
            else
            {
                report.Counts.SupervisorsCreated++;
            }

            return found;
        }

        var current = phoneOf(found);
        if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(current) && current != phone)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "AVANCE ODN", null, "TELÉFONO RESPONSABLE",
                $"El {kind} '{name}' aparece con teléfonos distintos ({current} y {phone}). Se conserva {current}."));
        }

        fillPhone(found, phone);
        return found;
    }

    // ------------------------------------------------------------------ etapas

    private static async Task ApplyStagesAsync(ApplicationDbContext db, Site site, ParsedNode node, CancellationToken ct)
    {
        if (node.Stages.Count == 0)
        {
            return;
        }

        var stages = await db.SiteStages.Where(s => s.SiteId == site.Id).ToListAsync(ct);
        foreach (var (code, status) in node.Stages)
        {
            var stage = stages.FirstOrDefault(s => s.Code == code);

            // No se pisan estados que el usuario ya cambió: solo se completan las etapas pendientes.
            if (stage is not null && stage.Status == StageStatus.Pending && status != StageStatus.Pending)
            {
                stage.Status = status;
            }
        }
    }

    // ------------------------------------------------------------------ feeder

    private async Task ApplyFeederAsync(ApplicationDbContext db, Project project, ParsedFeeder feeder, MaterialIndex index,
        List<Contractor> contractors, ImportReport report, string reference, DateOnly today, CancellationToken ct)
    {
        var contractor = FindOrCreate(contractors, feeder.Brigade, null, report,
            name => new Contractor { Name = name }, db.Contractors, c => c.Name, (_, _) => { }, _ => null, "contratista");

        var lines = await db.FeederLines.Where(l => l.ProjectId == project.Id).Include(l => l.Entries).ToListAsync(ct);
        foreach (var (kind, data) in feeder.Lines)
        {
            var line = lines.FirstOrDefault(l => l.Kind == kind);
            if (line is null)
            {
                line = new FeederLine { ProjectId = project.Id, Kind = kind, PlannedQuantity = data.Planned, Contractor = contractor };
                db.FeederLines.Add(line);
                lines.Add(line);
            }
            else
            {
                line.PlannedQuantity = data.Planned;
                line.Contractor ??= contractor;
            }

            var current = line.Entries.Where(e => !e.IsDeleted).Sum(e => e.Quantity);
            var delta = data.Installed - current;
            if (delta > 0)
            {
                line.Entries.Add(new FeederEntry
                {
                    Date = today,
                    Quantity = delta,
                    Notes = reference,
                    IsOpeningBalance = true,
                });
            }
            else if (delta < 0)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "AVANCE FEEDER", null, kind.ToString(),
                    $"El sistema ya tiene más instalado ({current:0.###}) que el Excel ({data.Installed:0.###}). No se modifica."));
            }

            report.Counts.FeederLinesSaved++;
        }

        // Materiales usados en el feeder: movimientos "instalado" sin nodo.
        var feederInstalled = (await db.InventoryMovements
                .Where(m => m.ProjectId == project.Id && m.SiteId == null && m.Type == MovementType.Installed && !m.IsVoided)
                .Select(m => new { m.MaterialId, m.Quantity })
                .ToListAsync(ct))
            .GroupBy(m => m.MaterialId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        foreach (var q in feeder.Materials.Where(q => q.Installed is > 0))
        {
            var material = index.Resolve(q);
            var delta = q.Installed!.Value - feederInstalled.GetValueOrDefault(material.Id);
            if (delta > 0)
            {
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProjectId = project.Id,
                    Type = MovementType.Installed,
                    MaterialId = material.Id,
                    Quantity = delta,
                    ContractorId = contractor?.Id,
                    Date = today,
                    Reference = reference,
                    Notes = "Material utilizado en el feeder (acumulado a la fecha de importación).",
                    IsOpeningBalance = true,
                });
                report.Counts.InstalledMovementsCreated++;
            }
        }
    }

    // ------------------------------------------------------------------ validación de totales

    private async Task BuildSystemChecksAsync(ImportReport report, ParsedWorkbook workbook, int projectId,
        List<int> siteIds, int importedNodes, CancellationToken ct)
    {
        var totals = await quantities.GetGroupTotalsBySiteAsync(projectId, ct);
        report.TotalChecks.Add(new TotalCheck("Nodos importados", workbook.Nodes.Count, importedNodes));

        foreach (var total in workbook.Totals)
        {
            decimal system = 0;
            foreach (var siteId in siteIds)
            {
                if (totals.TryGetValue(siteId, out var groups) && groups.TryGetValue(total.Group, out var pair))
                {
                    system += total.IsProjected ? pair.Projected : pair.Installed;
                }
            }

            report.TotalChecks.Add(new TotalCheck(total.Name, total.Value, system));
        }

        if (workbook.Feeder is { } feeder)
        {
            await using var db = factory.CreateDbContext();
            var lines = await db.FeederLines.AsNoTracking()
                .Where(l => l.ProjectId == projectId && l.Kind <= FeederItemKind.Cable96)
                .Include(l => l.Entries)
                .ToListAsync(ct);

            if (feeder.ExcelPlannedMeters is { } planned)
            {
                report.TotalChecks.Add(new TotalCheck("Feeder · metros proyectados", planned, lines.Sum(l => l.PlannedQuantity)));
            }

            if (feeder.ExcelInstalledMeters is { } installed)
            {
                report.TotalChecks.Add(new TotalCheck("Feeder · metros instalados", installed,
                    lines.Sum(l => l.Entries.Where(e => !e.IsDeleted).Sum(e => e.Quantity))));
            }
        }
    }

    // ------------------------------------------------------------------ índice de materiales

    private sealed class MaterialIndex
    {
        private readonly ApplicationDbContext _db;
        private readonly Dictionary<string, Material> _byKey = [];

        private MaterialIndex(ApplicationDbContext db) => _db = db;

        public int Created { get; private set; }

        public static async Task<MaterialIndex> LoadAsync(ApplicationDbContext db, CancellationToken ct)
        {
            var index = new MaterialIndex(db);
            var materials = await db.Materials.ToListAsync(ct);
            foreach (var material in materials)
            {
                index._byKey[NameNormalizer.Key(material.Name)] = material;
            }

            foreach (var alias in await db.MaterialAliases.ToListAsync(ct))
            {
                var owner = materials.FirstOrDefault(m => m.Id == alias.MaterialId);
                if (owner is not null)
                {
                    index._byKey.TryAdd(alias.AliasKey, owner);
                }
            }

            return index;
        }

        /// <summary>Busca por nombre o alias; si no existe, crea el material.</summary>
        public Material Resolve(ParsedQuantity quantity)
        {
            var key = NameNormalizer.Key(quantity.MaterialName);
            if (_byKey.TryGetValue(key, out var found))
            {
                return found;
            }

            var material = new Material
            {
                Name = quantity.MaterialName.Trim(),
                Category = quantity.Category,
                Group = quantity.Group,
                Unit = quantity.Unit,
                LengthMeters = quantity.LengthMeters,
                TracksStock = quantity.Category is not (MaterialCategory.Crossing or MaterialCategory.Activity),
            };
            _db.Materials.Add(material);
            _byKey[key] = material;
            Created++;
            return material;
        }
    }
}
