namespace Tekcom.Gestion.Application.Services;

/// <summary>Los tres colores de los gráficos del dashboard, en formato #RRGGBB.</summary>
/// <param name="Done">Finalizado, 100 % y la serie principal.</param>
/// <param name="InProgress">En avance y la serie secundaria.</param>
/// <param name="Alert">Sin iniciar y los números de alerta.</param>
public sealed record DashboardColors(string Done, string InProgress, string Alert)
{
    /// <summary>Los colores de referencia: verde, verde claro y rojo.</summary>
    public static DashboardColors Default { get; } = new("#54A32E", "#92D14F", "#EF472D");
}

/// <summary>Formas en que se puede dibujar un gráfico de categorías.</summary>
public enum ChartKind
{
    Bars,
    Columns,
    Donut,
    Pie,
}

/// <summary>Tipo de gráfico de cada panel de categorías del dashboard.</summary>
public sealed record DashboardCharts(ChartKind Installation, ChartKind Brigades, ChartKind Stages, ChartKind Nodes, ChartKind States)
{
    /// <summary>Los tipos de fábrica: barras, barras, barras, columnas y dona.</summary>
    public static DashboardCharts Default { get; } = new(ChartKind.Bars, ChartKind.Bars, ChartKind.Bars, ChartKind.Columns, ChartKind.Donut);
}

public interface IAppearanceService
{
    Task<DashboardColors> GetColorsAsync(CancellationToken ct = default);

    /// <summary>Guarda los colores. Cada uno debe ser #RRGGBB.</summary>
    Task SaveColorsAsync(DashboardColors colors, CancellationToken ct = default);

    Task ResetColorsAsync(CancellationToken ct = default);

    Task<DashboardCharts> GetChartsAsync(CancellationToken ct = default);

    /// <summary>Guarda el tipo de gráfico de cada panel.</summary>
    Task SaveChartsAsync(DashboardCharts charts, CancellationToken ct = default);

    Task ResetChartsAsync(CancellationToken ct = default);
}

public interface IGoalsService
{
    /// <summary>Meta de construcción de fábrica: 1,000 home passes por semana.</summary>
    const int DefaultWeeklyHp = 1000;

    Task<int> GetWeeklyHpGoalAsync(CancellationToken ct = default);
    Task SaveWeeklyHpGoalAsync(int homePassesPerWeek, CancellationToken ct = default);
}
