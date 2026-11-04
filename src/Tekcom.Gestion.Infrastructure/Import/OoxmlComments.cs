using System.IO.Compression;
using System.Xml.Linq;

namespace Tekcom.Gestion.Infrastructure.Import;

/// <summary>
/// Lee los comentarios de celda directamente del paquete .xlsx. Los comentarios "con hilo" de Excel
/// se guardan como texto simple y ClosedXML devuelve el texto vacío, así que se leen aquí.
/// </summary>
internal static class OoxmlComments
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Nombre de hoja → (referencia de celda → texto del comentario).</summary>
    public static Dictionary<string, Dictionary<string, string>> Read(Stream xlsx)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            xlsx.Position = 0;
            using var zip = new ZipArchive(xlsx, ZipArchiveMode.Read, leaveOpen: true);

            var workbook = Load(zip, "xl/workbook.xml");
            var workbookRels = Load(zip, "xl/_rels/workbook.xml.rels");
            if (workbook is null || workbookRels is null)
            {
                return result;
            }

            var sheetTargets = workbookRels.Root!.Elements(PackageRel + "Relationship")
                .ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);

            foreach (var sheet in workbook.Descendants(Main + "sheet"))
            {
                var name = (string?)sheet.Attribute("name");
                var rid = (string?)sheet.Attribute(Rel + "id");
                if (name is null || rid is null || !sheetTargets.TryGetValue(rid, out var target))
                {
                    continue;
                }

                var sheetPath = Resolve("xl/", target);
                var rels = Load(zip, $"{Path.GetDirectoryName(sheetPath)!.Replace('\\', '/')}/_rels/{Path.GetFileName(sheetPath)}.rels");
                var commentsTarget = rels?.Root?.Elements(PackageRel + "Relationship")
                    .FirstOrDefault(e => ((string?)e.Attribute("Type") ?? string.Empty).EndsWith("/comments", StringComparison.Ordinal))
                    ?.Attribute("Target")?.Value;
                if (commentsTarget is null)
                {
                    continue;
                }

                var comments = Load(zip, Resolve(Path.GetDirectoryName(sheetPath)!.Replace('\\', '/') + "/", commentsTarget));
                if (comments is null)
                {
                    continue;
                }

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var comment in comments.Descendants(Main + "comment"))
                {
                    var reference = (string?)comment.Attribute("ref");
                    if (reference is null)
                    {
                        continue;
                    }

                    var text = string.Concat(comment.Descendants(Main + "t").Select(t => t.Value));
                    map[reference] = text;
                }

                result[name] = map;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException or KeyNotFoundException)
        {
            // Si el paquete no se puede leer, simplemente no hay comentarios que importar.
        }
        finally
        {
            xlsx.Position = 0;
        }

        return result;
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path.TrimStart('/'));
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>Resuelve una ruta relativa ("../comments1.xml") contra una carpeta base.</summary>
    private static string Resolve(string baseFolder, string target)
    {
        if (target.StartsWith('/'))
        {
            return target.TrimStart('/');
        }

        var parts = (baseFolder.TrimEnd('/') + "/" + target).Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        var stack = new List<string>();
        foreach (var part in parts)
        {
            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
            }
            else if (part != ".")
            {
                stack.Add(part);
            }
        }

        return string.Join('/', stack);
    }
}
