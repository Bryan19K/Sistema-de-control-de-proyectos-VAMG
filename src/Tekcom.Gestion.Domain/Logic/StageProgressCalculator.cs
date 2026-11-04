using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Logic;

public sealed record StageWeight(StageStatus Status, decimal PlannedDays, bool CountsForProgress);

/// <summary>
/// Avance del nodo ponderado por días planificados: días de las etapas finalizadas ÷ días totales.
/// Las etapas "No aplica" y las informativas (CAES) no cuentan. Una etapa "En proceso" no suma.
/// </summary>
public static class StageProgressCalculator
{
    public static decimal Percent(IEnumerable<StageWeight> stages)
    {
        decimal total = 0;
        decimal done = 0;
        foreach (var stage in stages)
        {
            if (!stage.CountsForProgress || stage.Status == StageStatus.NotApplicable || stage.PlannedDays <= 0)
            {
                continue;
            }

            total += stage.PlannedDays;
            if (stage.Status == StageStatus.Finished)
            {
                done += stage.PlannedDays;
            }
        }

        return total == 0 ? 0 : Math.Round(done / total * 100m, 1);
    }
}
