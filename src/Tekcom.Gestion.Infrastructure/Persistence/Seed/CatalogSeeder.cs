using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Infrastructure.Persistence.Seed;

/// <summary>
/// Datos base que el sistema necesita para funcionar: tipos de proyecto, etapas del flujo del nodo
/// y el catálogo de materiales del flujo FTTH. Es idempotente: se puede ejecutar en cada arranque.
/// </summary>
public static class CatalogSeeder
{
    public static readonly int[] FcLengths = [5, 50, 80, 100, 120, 150, 180, 200, 250, 300, 350, 400, 450, 500];
    public static readonly int[] MpoLengths = [100, 150, 200, 250, 300, 350, 400, 450, 500];

    public const string FtthCode = "FTTH";
    public const string GeneralCode = "GENERAL";

    public static async Task EnsureAllAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        await EnsureProjectTypesAsync(db, ct);
        await EnsureStageDefinitionsAsync(db, ct);
        await EnsureBaseCatalogAsync(db, ct);
        await EnsureExportTemplatesAsync(db, ct);
    }

    public static async Task EnsureExportTemplatesAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (!await db.ExportTemplates.AnyAsync(t => t.IsSystem, ct))
        {
            db.ExportTemplates.Add(new ExportTemplate { Name = "Estándar TEK-COM", IsSystem = true });
            await db.SaveChangesAsync(ct);
        }
    }

    public static async Task EnsureProjectTypesAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (!await db.ProjectTypes.AnyAsync(t => t.Code == FtthCode, ct))
        {
            db.ProjectTypes.Add(new ProjectType
            {
                Code = FtthCode,
                Name = "FTTH / Telecomunicaciones",
                Modules = ProjectModules.All,
                IsSystem = true,
            });
        }

        if (!await db.ProjectTypes.AnyAsync(t => t.Code == GeneralCode, ct))
        {
            db.ProjectTypes.Add(new ProjectType
            {
                Code = GeneralCode,
                Name = "General",
                Modules = ProjectModules.General,
                IsSystem = true,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Etapas del flujo FTTH (docs/FLUJO_OPERATIVO.md). Los días son configurables.
    /// CAES no es una etapa: es una casilla informativa del nodo.
    /// </summary>
    public static async Task EnsureStageDefinitionsAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var ftth = await db.ProjectTypes.FirstAsync(t => t.Code == FtthCode, ct);
        var existing = await db.StageDefinitions
            .Where(d => d.ProjectTypeId == ftth.Id)
            .Select(d => d.Code)
            .ToListAsync(ct);

        var definitions = new (StageCode Code, string Name, bool Counts, decimal Days)[]
        {
            (StageCode.Layout, "Replanteo del nodo", true, 1),
            (StageCode.MaterialDelivery, "Entrega de materiales", true, 1),
            (StageCode.FcCable, "Instalación de cable FC", true, 1),
            (StageCode.MpoCable, "Instalación de cable MPO", true, 1),
            (StageCode.Fat, "Instalación de FAT (sub-box y end-box)", true, 1),
            (StageCode.HubAndXBox, "Instalación de Hub-box y X-box", true, 1),
            (StageCode.Fusion, "Fusiones en X-box", true, 1),
            (StageCode.AmericanCrossings, "Cruces americanos", true, 1),
            (StageCode.FatMeasurements, "Mediciones de FATs", true, 1),
            (StageCode.Corrections, "Correcciones estéticas y técnicas", true, 1),
            (StageCode.Atp, "ATP (entregable al cliente)", true, 1),
            (StageCode.FinalReception, "Recepción final", true, 1),
        };

        foreach (var (code, name, counts, days) in definitions)
        {
            if (existing.Contains(code))
            {
                continue;
            }

            db.StageDefinitions.Add(new StageDefinition
            {
                ProjectTypeId = ftth.Id,
                Code = code,
                Name = name,
                SortOrder = (int)code,
                CountsForProgress = counts,
                PlannedDays = days,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task EnsureBaseCatalogAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        foreach (var length in FcLengths)
        {
            await EnsureMaterialAsync(db, $"Cable FC SC/APC {length} m", MaterialCategory.FiberCable,
                QuantityGroup.FcCable, "UND", length, ct, $"FO {length} M", $"FO {length}M");
        }

        foreach (var length in MpoLengths)
        {
            await EnsureMaterialAsync(db, $"Cable MPO {length} m", MaterialCategory.MpoCable,
                QuantityGroup.MpoCable, "UND", length, ct, $"MPO {length}");
        }

        await EnsureMaterialAsync(db, "NAP de paso", MaterialCategory.NetworkElement, QuantityGroup.SubBox, "UND", null, ct,
            "SUB-BOX", "SUB BOX", "SUBBOX");
        await EnsureMaterialAsync(db, "NAP final", MaterialCategory.NetworkElement, QuantityGroup.EndBox, "UND", null, ct,
            "END-BOX", "END BOX", "ENDBOX");
        await EnsureMaterialAsync(db, "Hub-box", MaterialCategory.NetworkElement, QuantityGroup.HubBox, "UND", null, ct,
            "HBBOX", "HB-BOX", "HUB BOX");
        await EnsureMaterialAsync(db, "X-box", MaterialCategory.NetworkElement, QuantityGroup.XBox, "UND", null, ct,
            "XBOX", "X BOX");

        await EnsureMaterialAsync(db, "Cruce americano", MaterialCategory.Crossing, QuantityGroup.Crossing, "UND", null, ct,
            "CRUCE AMERICANOS", "CRUCES AMERICANOS", "CRUCE AMERICANO");
        await EnsureMaterialAsync(db, "Fusión en X-box", MaterialCategory.Activity, QuantityGroup.Fusion, "fusión", null, ct,
            "FUSIONES XBOX", "FUSIONES EN XBOX");

        // Aro de sujeción y trébol se llevan por separado (decisión 15 del flujo).
        await EnsureMaterialAsync(db, "Aro de sujeción", MaterialCategory.Hardware, QuantityGroup.None, "UND", null, ct,
            "ARO DE SUJECION");
        await EnsureMaterialAsync(db, "Trébol", MaterialCategory.Hardware, QuantityGroup.None, "UND", null, ct,
            "TREBOL", "TREBOLES");

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Busca el material por nombre o alias; si no existe lo crea con sus alias. Devuelve el material.
    /// </summary>
    public static async Task<Material> EnsureMaterialAsync(
        ApplicationDbContext db,
        string name,
        MaterialCategory category,
        QuantityGroup group,
        string unit,
        int? lengthMeters,
        CancellationToken ct,
        params string[] aliases)
    {
        var key = NameNormalizer.Key(name);
        var material = await FindByKeyAsync(db, key, ct);
        if (material is null)
        {
            material = new Material
            {
                Name = name,
                Category = category,
                Group = group,
                Unit = unit,
                LengthMeters = lengthMeters,
                TracksStock = category is not (MaterialCategory.Crossing or MaterialCategory.Activity),
            };
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
        }

        foreach (var alias in aliases)
        {
            var aliasKey = NameNormalizer.Key(alias);
            if (aliasKey.Length == 0 || aliasKey == NameNormalizer.Key(material.Name))
            {
                continue;
            }

            var owner = await FindByKeyAsync(db, aliasKey, ct);
            if (owner is null)
            {
                db.MaterialAliases.Add(new MaterialAlias { MaterialId = material.Id, Alias = alias, AliasKey = aliasKey });
                await db.SaveChangesAsync(ct);
            }
        }

        return material;
    }

    /// <summary>Resuelve un nombre de material (nombre propio o alias) sin crear nada.</summary>
    public static async Task<Material?> FindByKeyAsync(ApplicationDbContext db, string key, CancellationToken ct = default)
    {
        if (key.Length == 0)
        {
            return null;
        }

        var materials = await db.Materials.ToListAsync(ct);
        var direct = materials.FirstOrDefault(m => NameNormalizer.Key(m.Name) == key);
        if (direct is not null)
        {
            return direct;
        }

        var alias = await db.MaterialAliases.FirstOrDefaultAsync(a => a.AliasKey == key, ct);
        return alias is null ? null : materials.FirstOrDefault(m => m.Id == alias.MaterialId);
    }
}
