namespace Tekcom.Gestion.Application.Common;

/// <summary>Cálculos pequeños del dashboard y del saludo de Inicio.</summary>
public static class DashboardMath
{
    /// <summary>Variación porcentual contra el período anterior; vacío si no había con qué comparar.</summary>
    public static decimal? PercentChange(decimal current, decimal previous) =>
        previous <= 0 ? null : Math.Round((current - previous) / previous * 100m, 1);

    /// <summary>Texto del chip: "+22.0 %" o "-8.5 %".</summary>
    public static string ChangeText(decimal? change) =>
        change is null ? string.Empty : $"{(change >= 0 ? "+" : "-")}{Math.Abs(change.Value):0.#} %";

    /// <summary>Parte usada de un fondo asignado, de 0 a 100 (se pasa de 100 si hay sobregiro).</summary>
    public static decimal UsedPercent(decimal spent, decimal assigned) =>
        assigned <= 0 ? 0 : Math.Round(spent / assigned * 100m, 1);

    /// <summary>"Bienvenido, Nombre Apellido"; sin nombre usa lo que va antes de la arroba del correo.</summary>
    public static string Welcome(string? fullName, string? email)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? email?.Split('@')[0] : fullName.Trim();
        return string.IsNullOrWhiteSpace(name) ? "Bienvenido" : $"Bienvenido, {name}";
    }

    /// <summary>Una orden está liquidada cuando su estado de liquidación dice realizada o liquidada.</summary>
    public static bool IsLiquidated(string? status) =>
        status is not null && (status.Contains("REALIZ", StringComparison.OrdinalIgnoreCase) || status.Contains("LIQUID", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Fecha estimada de cierre por el ritmo que lleva el proyecto: avance ÷ días desde que empezó la instalación.
    /// Es solo informativa. Sin al menos una semana de trabajo o sin avance no se estima.
    /// </summary>
    public static DateOnly? EstimatedClose(decimal progress, DateOnly? firstInstall, DateOnly today)
    {
        if (progress >= 100)
        {
            return today;
        }

        if (firstInstall is null || progress <= 0)
        {
            return null;
        }

        var days = today.DayNumber - firstInstall.Value.DayNumber;
        if (days < 7)
        {
            return null;
        }

        var perDay = progress / days;
        return today.AddDays((int)Math.Ceiling((double)((100 - progress) / perDay)));
    }

    /// <summary>Costo por unidad (home pass, kilómetro, FAT); vacío si todavía no hay unidades.</summary>
    public static decimal? UnitCost(decimal total, decimal units) => units <= 0 ? null : Math.Round(total / units, 2);
}
