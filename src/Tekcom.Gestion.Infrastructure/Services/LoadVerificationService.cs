using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>
/// Comprueba que cada Excel entregado (control FTTH con bodega, control de proyectos NextWave, materiales NextWave, gastos,
/// preciario y contratistas) esté cargado, comparando conteos y montos con las cifras de esos archivos.
/// Como todo es editable, una diferencia no es un error: indica que se editó o que falta volver a cargar.
/// </summary>
public sealed class LoadVerificationService(IDbContextFactory<ApplicationDbContext> factory) : ILoadVerificationService
{
    private static readonly string[] Names =
    [
        "PROYECTO_FTTH_2026_62.xlsx",
        "TEK-COM - CONTROL DE PROYECTOS 20.xlsx",
        "PROYECTOS NEXT-WAVE 2026 9.xlsx",
        "GASTOS PROYECTO FTTH 4.xlsx",
        "PRECIARIO FINAL - CONTRATISTAS.xlsx",
        "BASE DE DATOS CONTRATISTAS FTTH - TEKCOM.xlsx",
    ];

    public async Task<LoadVerification> VerifyAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var result = new LoadVerification();

        // ------------------------------------------------------------ 1. FTTH + bodega
        var ftth = new List<LoadCheck>();
        var f = Names[0];
        var ftthProjects = await db.Projects.AsNoTracking().Where(p => p.ProjectType!.Code == CatalogSeeder.FtthCode).Select(p => p.Id).ToListAsync(ct);
        ftth.Add(Exact(f, "Nodos", 20, await db.Sites.CountAsync(s => ftthProjects.Contains(s.ProjectId), ct)));
        ftth.Add(Exact(f, "Líneas de feeder", 9, await db.FeederLines.CountAsync(l => ftthProjects.Contains(l.ProjectId), ct)));
        var movements = db.InventoryMovements.AsNoTracking().Where(m => !m.IsVoided && m.Reference != null);
        ftth.Add(Exact(f, "Unidades recibidas del proveedor (bodega)", 32714m,
            await movements.Where(m => m.Reference!.StartsWith("Excel · Ingreso")).SumAsync(m => (decimal?)m.Quantity, ct) ?? 0));
        ftth.Add(Exact(f, "Unidades entregadas a brigadas (bodega)", 48274m,
            await movements.Where(m => m.Reference!.StartsWith("Excel · Entrega")).SumAsync(m => (decimal?)m.Quantity, ct) ?? 0));
        ftth.Add(Exact(f, "SFP con serial", 244, await db.Transceivers.CountAsync(ct)));
        result.Files.Add(Summarize(f, "Nodos, avance, feeder, bodega y seriales", ftth));

        // ------------------------------------------------------------ 2. Control de proyectos NextWave
        var nw = new List<LoadCheck>();
        f = Names[1];
        var buildingIds = await db.Projects.AsNoTracking().Where(p => p.ProjectType!.Code == CatalogSeeder.BuildingCode).Select(p => p.Id).ToListAsync(ct);
        nw.Add(Exact(f, "Proyectos de edificios", 5, buildingIds.Count));
        nw.Add(Exact(f, "Fases", 25, await db.ProjectPhases.CountAsync(ct)));
        nw.Add(Exact(f, "Apartamentos", 273, await db.BuildingRecords.CountAsync(r => r.Kind == BuildingRecordKind.Unit, ct)));
        nw.Add(Exact(f, "Infraestructura por edificio", 5, await db.BuildingRecords.CountAsync(r => r.Kind == BuildingRecordKind.Infrastructure, ct)));
        nw.Add(Exact(f, "Resumen por nivel", 35, await db.BuildingRecords.CountAsync(r => r.Kind == BuildingRecordKind.Level, ct)));
        nw.Add(AtLeast(f, "Proyectos con materiales cargados", 5,
            await db.InventoryMovements.Where(m => buildingIds.Contains(m.ProjectId) && !m.IsVoided).Select(m => m.ProjectId).Distinct().CountAsync(ct)));
        result.Files.Add(Summarize(f, "Cinco edificios con fases, apartamentos, infraestructura, niveles y materiales", nw));

        // ------------------------------------------------------------ 3. Materiales NextWave
        var materials = new List<LoadCheck>();
        f = Names[2];
        var dedicated = await db.InventoryMovements.AsNoTracking()
            .Where(m => !m.IsVoided && m.Reference != null && m.Reference.StartsWith("Excel · NextWave · Control"))
            .Select(m => m.ProjectId).Distinct().CountAsync(ct);
        materials.Add(Exact(f, "Proyectos con su control de materiales (Florencia, Collections y San Benito 53)", 3, dedicated));
        result.Files.Add(Summarize(f, "Material asignado, instalado y devuelto de tres proyectos", materials));

        // ------------------------------------------------------------ 4. Gastos
        var expenses = new List<LoadCheck>();
        f = Names[3];
        var entries = db.ExpenseEntries.AsNoTracking().Where(e => !e.SourceKey.StartsWith("MANUAL"));
        expenses.Add(Exact(f, "Compras", 16, await entries.CountAsync(e => e.Kind == ExpenseKind.Purchase, ct)));
        expenses.Add(Exact(f, "Compras con IVA ($)", 2132.02m, await entries.Where(e => e.Kind == ExpenseKind.Purchase).SumAsync(e => (decimal?)e.Total, ct) ?? 0));
        expenses.Add(Exact(f, "Gastos de caja chica", 57, await entries.CountAsync(e => e.Kind == ExpenseKind.PettyCash, ct)));
        expenses.Add(Exact(f, "Caja chica ($)", 1529.02m, await entries.Where(e => e.Kind == ExpenseKind.PettyCash).SumAsync(e => (decimal?)e.Total, ct) ?? 0));
        expenses.Add(Exact(f, "Viáticos", 13, await entries.CountAsync(e => e.Kind == ExpenseKind.Travel, ct)));
        expenses.Add(Exact(f, "Viáticos ($)", 339m, await entries.Where(e => e.Kind == ExpenseKind.Travel).SumAsync(e => (decimal?)e.Total, ct) ?? 0));
        expenses.Add(Exact(f, "Fondos", 7, await db.ExpenseFunds.CountAsync(x => !x.SourceKey.StartsWith("MANUAL"), ct)));
        result.Files.Add(Summarize(f, "Compras, caja chica y viáticos", expenses));

        // ------------------------------------------------------------ 5. Preciario
        var prices = new List<LoadCheck>();
        f = Names[4];
        prices.Add(Exact(f, "Órdenes por nodo", 24, await db.ContractOrders.CountAsync(ct)));
        prices.Add(Exact(f, "Valor de las órdenes con IVA ($)", 177373.28m, await db.ContractOrders.SumAsync(o => (decimal?)o.Total, ct) ?? 0));
        prices.Add(Exact(f, "Precios target", 35, await db.PriceItems.CountAsync(ct)));
        prices.Add(AtLeast(f, "Materiales con código ERP", 37, await db.Materials.CountAsync(m => m.Code != null && m.Code.StartsWith("NW-"), ct)));
        result.Files.Add(Summarize(f, "Precios target, órdenes por nodo y códigos ERP", prices));

        // ------------------------------------------------------------ 6. Contratistas
        var contractors = new List<LoadCheck>();
        f = Names[5];
        contractors.Add(Exact(f, "Personal de campo", 54, await db.ContractorPeople.CountAsync(ct)));
        contractors.Add(AtLeast(f, "Contratistas por cotizar o que dijeron que no", 10, await db.Contractors.CountAsync(c => c.Stage != ContractorStage.Active, ct)));
        contractors.Add(AtLeast(f, "Contratistas con datos completos (NRC)", 7, await db.Contractors.CountAsync(c => c.Nrc != null, ct)));
        var optical = await db.Contractors.AsNoTracking().Where(c => c.Name.ToUpper().Contains("OPTICAL")).ToListAsync(ct);
        var opticalOk = optical.All(c => !c.IsActive) && !await db.ContractorPeople.AnyAsync(p => p.Contractor!.Name.ToUpper().Contains("OPTICAL"), ct);
        contractors.Add(new LoadCheck(f, "Optical Precision dada de baja y sin personal", "sí", opticalOk ? "sí" : "no", opticalOk ? LoadCheckState.Ok : LoadCheckState.Different));
        result.Files.Add(Summarize(f, "Contratistas y personal con póliza o afiliación", contractors));

        return result;
    }

    private static LoadFileStatus Summarize(string file, string description, List<LoadCheck> checks) =>
        // Cargado = ninguna cifra del archivo está en cero; las diferencias (por ediciones) se cuentan aparte.
        new(file, description, checks.All(c => c.State != LoadCheckState.Missing), checks.Count(c => c.State == LoadCheckState.Different), checks);

    private static LoadCheck Exact(string file, string name, decimal expected, decimal actual) =>
        new(file, name, Format(expected), Format(actual),
            // En dólares se tolera 2 centavos: el Excel redondea distinto que la suma de sus filas.
            Math.Abs(actual - expected) <= (expected == Math.Truncate(expected) ? 0 : 0.02m) ? LoadCheckState.Ok : actual == 0 ? LoadCheckState.Missing : LoadCheckState.Different);

    private static LoadCheck AtLeast(string file, string name, decimal expected, decimal actual) =>
        new(file, name, $"≥ {Format(expected)}", Format(actual),
            actual >= expected ? LoadCheckState.Ok : actual == 0 ? LoadCheckState.Missing : LoadCheckState.Different);

    private static string Format(decimal value) =>
        value == Math.Truncate(value) ? value.ToString("#,##0", CultureInfo.InvariantCulture) : value.ToString("#,##0.##", CultureInfo.InvariantCulture);
}
