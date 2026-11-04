using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Import;

namespace Tekcom.Gestion.Infrastructure.Import;

/// <summary>
/// Detecta qué trae el Excel (control FTTH, bodega, proyectos NextWave, gastos, preciario) y lo carga en orden:
/// primero el control FTTH (crea el proyecto y los nodos), después lo que depende de él.
/// </summary>
public sealed class DataImportCoordinator(IExcelImportService ftth, IEnumerable<IDataPackImporter> importers) : IDataImportCoordinator
{
    public async Task<List<PackAnalysis>> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        var packs = new List<PackAnalysis>();

        using (var book = Xl.Open(file))
        {
            if (book is null)
            {
                throw new BusinessRuleException("No se pudo abrir el archivo. Verifique que sea un Excel (.xlsx) válido.");
            }

            if (Xl.SheetContaining(book, "AVANCE ODN") is not null)
            {
                var analysis = await ftth.AnalyzeAsync(new MemoryStream(file), fileName, ct);
                packs.Add(new PackAnalysis
                {
                    Kind = PackKind.FtthControl,
                    Title = "Control de avance FTTH (nodos, avance y feeder)",
                    Description = "Nodos, etapas, cantidades proyectadas e instaladas y el feeder.",
                    Report = analysis.Report,
                    Payload = analysis,
                });
            }
        }

        foreach (var importer in importers.OrderBy(i => (int)i.Kind))
        {
            var pack = await importer.AnalyzeAsync(file, fileName, ct);
            if (pack is not null)
            {
                packs.Add(pack);
            }
        }

        return packs.OrderBy(p => (int)p.Kind).ToList();
    }

    public async Task<List<PackAnalysis>> ApplyAsync(List<PackAnalysis> packs, ImportTarget target, CancellationToken ct = default)
    {
        var done = new List<PackAnalysis>();
        int? projectId = target.ProjectId;

        foreach (var pack in packs.OrderBy(p => (int)p.Kind))
        {
            ImportReport report;
            if (pack.Kind == PackKind.FtthControl)
            {
                report = await ftth.ApplyAsync((ImportAnalysis)pack.Payload!, target, ct);
                projectId = report.ProjectId ?? projectId;
            }
            else
            {
                var importer = importers.First(i => i.Kind == pack.Kind);
                report = await importer.ApplyAsync(pack, projectId, ct);
            }

            done.Add(new PackAnalysis
            {
                Kind = pack.Kind, Title = pack.Title, Description = pack.Description, Report = report,
                NeedsProject = pack.NeedsProject, Payload = pack.Payload,
            });
        }

        return done;
    }
}
