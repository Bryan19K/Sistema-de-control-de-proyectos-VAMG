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

public interface IAppearanceService
{
    Task<DashboardColors> GetColorsAsync(CancellationToken ct = default);

    /// <summary>Guarda los colores. Cada uno debe ser #RRGGBB.</summary>
    Task SaveColorsAsync(DashboardColors colors, CancellationToken ct = default);

    Task ResetColorsAsync(CancellationToken ct = default);
}
