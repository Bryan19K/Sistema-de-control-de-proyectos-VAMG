using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Infrastructure.Import;

/// <summary>Lectura tolerante de celdas de Excel (texto, números y fechas escritos de cualquier forma).</summary>
internal static class Xl
{
    public static string Text(IXLCell cell)
    {
        try
        {
            return cell.IsEmpty() ? string.Empty : cell.GetFormattedString().Trim();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static string Text(IXLWorksheet sheet, int row, int col) => Text(sheet.Cell(row, col));

    public static decimal? Number(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        try
        {
            var value = cell.Value;
            if (value.IsNumber)
            {
                return (decimal)value.GetNumber();
            }

            if (value.IsBoolean || value.IsError)
            {
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }

        var text = Text(cell).Replace("$", "").Replace(" ", "").Replace(",", "");
        return decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    public static decimal? Number(IXLWorksheet sheet, int row, int col) => Number(sheet.Cell(row, col));

    public static DateOnly? Date(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        try
        {
            var value = cell.Value;
            if (value.IsDateTime)
            {
                return DateOnly.FromDateTime(value.GetDateTime());
            }

            if (value.IsNumber)
            {
                var n = value.GetNumber();
                if (n is > 20000 and < 80000)
                {
                    return DateOnly.FromDateTime(DateTime.FromOADate(n));
                }

                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }

        var text = Text(cell);
        string[] formats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yy", "d/M/yy"];
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static DateOnly? Date(IXLWorksheet sheet, int row, int col) => Date(sheet.Cell(row, col));

    public static IXLWorksheet? Sheet(IXLWorkbook book, string name)
    {
        var key = NameNormalizer.Key(name);
        return book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name) == key);
    }

    public static IXLWorksheet? SheetContaining(IXLWorkbook book, string part)
    {
        var key = NameNormalizer.Key(part);
        return book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name).Contains(key, StringComparison.Ordinal));
    }

    public static string TitleCase(string text)
    {
        var lower = text.Trim().ToLower(new CultureInfo("es-SV"));
        return CultureInfo.GetCultureInfo("es-SV").TextInfo.ToTitleCase(lower);
    }

    /// <summary>Abre el libro desde bytes (null si no es un .xlsx válido).</summary>
    public static XLWorkbook? Open(byte[] data)
    {
        try
        {
            return new XLWorkbook(new MemoryStream(data));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}

/// <summary>
/// Busca materiales por nombre o alias y crea los que no existen. Entiende los nombres del Excel de bodega
/// ("CABLE FC 200 METROS" = cable FC de 200 m del catálogo).
/// </summary>
internal sealed partial class MaterialResolver
{
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<string, Material> _byKey = [];

    public int Created { get; private set; }

    private MaterialResolver(ApplicationDbContext db) => _db = db;

    public static async Task<MaterialResolver> LoadAsync(ApplicationDbContext db, CancellationToken ct)
    {
        await CatalogSeeder.EnsureBaseCatalogAsync(db, ct);
        var resolver = new MaterialResolver(db);
        var materials = await db.Materials.ToListAsync(ct);
        foreach (var material in materials)
        {
            resolver._byKey[NameNormalizer.Key(material.Name)] = material;
        }

        foreach (var alias in await db.MaterialAliases.ToListAsync(ct))
        {
            var owner = materials.FirstOrDefault(m => m.Id == alias.MaterialId);
            if (owner is not null)
            {
                resolver._byKey.TryAdd(alias.AliasKey, owner);
            }
        }

        return resolver;
    }

    [GeneratedRegex(@"^(?:CABLE\s+)?FC\s+(?<n>\d+)\s*(?:M|MT|MTS|METROS)?$", RegexOptions.IgnoreCase)]
    private static partial Regex FcPattern();

    [GeneratedRegex(@"^(?:CABLE\s+)?(?:FC\s+)?MPO\s+(?<n>\d+)\s*(?:M|MT|MTS|METROS)?$", RegexOptions.IgnoreCase)]
    private static partial Regex MpoPattern();

    public static string NormalizeUnit(string? unit)
    {
        var u = (unit ?? string.Empty).Trim();
        return NameNormalizer.Key(u) switch
        {
            "" => "UND",
            "U" or "UN" or "UND" or "UNIDAD" or "UNIDADES" => "UND",
            "P" or "PAR" or "PARES" => "PAR",
            "M" or "MT" or "MTS" or "METRO" or "METROS" => "m",
            "ROLL" or "ROLLO" or "ROLLOS" => "Rollo",
            _ => u,
        };
    }

    /// <summary>Resuelve el material; <paramref name="model"/> y <paramref name="description"/> completan lo que esté vacío.</summary>
    public Material Resolve(string rawName, string? unit = null, string? model = null, string? description = null, string? erpCode = null)
    {
        var name = Regex.Replace(rawName.Trim(), @"\s+", " ");
        var material = Find(name) ?? Create(name, unit);
        if (!string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(material.Model))
        {
            material.Model = model.Trim();
        }

        if (!string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(material.Description))
        {
            material.Description = description.Trim().Length > 600 ? description.Trim()[..600] : description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(erpCode) && string.IsNullOrWhiteSpace(material.Code))
        {
            material.Code = erpCode.Trim();
        }

        return material;
    }

    private Material? Find(string name)
    {
        if (_byKey.TryGetValue(NameNormalizer.Key(name), out var direct))
        {
            return direct;
        }

        var fc = FcPattern().Match(name);
        if (fc.Success && _byKey.TryGetValue(NameNormalizer.Key($"FO {fc.Groups["n"].Value} M"), out var cable))
        {
            return cable;
        }

        var mpo = MpoPattern().Match(name);
        if (mpo.Success && _byKey.TryGetValue(NameNormalizer.Key($"MPO {mpo.Groups["n"].Value}"), out var mpoCable))
        {
            return mpoCable;
        }

        return null;
    }

    private Material Create(string name, string? unit)
    {
        var fc = FcPattern().Match(name);
        var mpo = MpoPattern().Match(name);
        Material material;
        if (fc.Success)
        {
            var length = int.Parse(fc.Groups["n"].Value, CultureInfo.InvariantCulture);
            material = new Material
            {
                Name = $"Cable FC SC/APC {length} m", Category = MaterialCategory.FiberCable, Group = QuantityGroup.FcCable,
                Unit = "UND", LengthMeters = length,
            };
            AddAlias(material, $"FO {length} M");
        }
        else if (mpo.Success)
        {
            var length = int.Parse(mpo.Groups["n"].Value, CultureInfo.InvariantCulture);
            material = new Material
            {
                Name = $"Cable MPO {length} m", Category = MaterialCategory.MpoCable, Group = QuantityGroup.MpoCable,
                Unit = "UND", LengthMeters = length,
            };
            AddAlias(material, $"MPO {length}");
        }
        else
        {
            material = new Material
            {
                Name = name.Length > 200 ? name[..200] : name,
                Category = GuessCategory(name),
                Unit = NormalizeUnit(unit),
            };
        }

        material.TracksStock = true;
        _db.Materials.Add(material);
        _byKey[NameNormalizer.Key(material.Name)] = material;
        foreach (var alias in material.Aliases)
        {
            _byKey.TryAdd(alias.AliasKey, material);
        }

        Created++;
        return material;
    }

    private static void AddAlias(Material material, string alias) =>
        material.Aliases.Add(new MaterialAlias { Alias = alias, AliasKey = NameNormalizer.Key(alias) });

    private static MaterialCategory GuessCategory(string name)
    {
        var k = NameNormalizer.Key(name);
        bool Has(params string[] parts) => parts.Any(p => k.Contains(p, StringComparison.Ordinal));

        if (Has("SPLITTER"))
        {
            return MaterialCategory.Closure;
        }

        if (Has("ODF"))
        {
            return MaterialCategory.Odf;
        }

        if (Has("CABLE DE FIBRA"))
        {
            return MaterialCategory.FeederCable;
        }

        if (Has("NAP", "XBOX", "HUB BOX", "HBBOX"))
        {
            return MaterialCategory.NetworkElement;
        }

        if (Has("HERRAJE", "ABRAZADERA", "PERNO", "GUARDACABO", "BRAZO", "SOPORTE", "ARO ", "TEMCLAN", "ALETA", "PREFORM"))
        {
            return MaterialCategory.Hardware;
        }

        if (Has("CINTA", "CINCHO", "FAJILLA", "VELCRO", "HEBILLA", "MANGA", "TUBO"))
        {
            return MaterialCategory.Consumable;
        }

        return MaterialCategory.Other;
    }
}

/// <summary>Busca o crea contratistas por nombre sin importar mayúsculas ni acentos.</summary>
internal sealed class ContractorResolver
{
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<string, Contractor> _byKey = [];

    public int Created { get; private set; }

    private ContractorResolver(ApplicationDbContext db) => _db = db;

    public static async Task<ContractorResolver> LoadAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var resolver = new ContractorResolver(db);
        foreach (var contractor in await db.Contractors.ToListAsync(ct))
        {
            resolver._byKey.TryAdd(NameNormalizer.Key(contractor.Name), contractor);
        }

        return resolver;
    }

    public Contractor? Resolve(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        var key = NameNormalizer.Key(clean);
        if (key.Length == 0)
        {
            return null;
        }

        if (_byKey.TryGetValue(key, out var found))
        {
            return found;
        }

        var contractor = new Contractor { Name = Xl.TitleCase(clean) };
        _db.Contractors.Add(contractor);
        _byKey[key] = contractor;
        Created++;
        return contractor;
    }
}
