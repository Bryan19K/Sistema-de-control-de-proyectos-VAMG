using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Logic;

public sealed record PaceResult(
    decimal ProgressPercent,
    decimal ElapsedPercent,
    PaceStatus Status,
    int DaysDelta,
    DateOnly? ProjectedEnd,
    int TotalDays,
    int ElapsedDays);

/// <summary>
/// Indicador de ritmo: % avance − % tiempo transcurrido.
/// Mayor a +margen = Adelantado · dentro de ±margen = En tiempo · menor a −margen = Atrasado.
/// Aplica a proyectos, planes, sprints y nodos.
/// </summary>
public static class PaceCalculator
{
    public static PaceResult Calculate(
        decimal progressPercent,
        DateOnly? start,
        DateOnly? end,
        DateOnly today,
        int marginPercent = 10)
    {
        progressPercent = Math.Clamp(progressPercent, 0m, 100m);

        if (start is null || end is null || end <= start)
        {
            return new PaceResult(progressPercent, 0, PaceStatus.Unknown, 0, null, 0, 0);
        }

        var totalDays = end.Value.DayNumber - start.Value.DayNumber;
        var elapsedDays = Math.Clamp(today.DayNumber - start.Value.DayNumber, 0, totalDays);
        var elapsedPercent = Math.Round(elapsedDays * 100m / totalDays, 1);

        if (progressPercent >= 100m)
        {
            return new PaceResult(progressPercent, elapsedPercent, PaceStatus.Completed, 0, end, totalDays, elapsedDays);
        }

        var diff = progressPercent - elapsedPercent;
        var status = diff > marginPercent
            ? PaceStatus.Ahead
            : diff < -marginPercent ? PaceStatus.Behind : PaceStatus.OnTime;

        // Días de adelanto (+) o atraso (−) respecto del tiempo transcurrido.
        var daysDelta = (int)Math.Round(diff / 100m * totalDays, MidpointRounding.AwayFromZero);

        // Cierre proyectado: si se mantiene el ritmo actual.
        DateOnly? projectedEnd = null;
        if (progressPercent > 0 && elapsedDays > 0)
        {
            var projectedTotal = (int)Math.Ceiling(elapsedDays / (double)(progressPercent / 100m));
            projectedEnd = start.Value.AddDays(projectedTotal);
        }

        return new PaceResult(progressPercent, elapsedPercent, status, daysDelta, projectedEnd, totalDays, elapsedDays);
    }
}
