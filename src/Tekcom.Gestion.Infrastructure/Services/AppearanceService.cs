using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>Colores del dashboard que el administrador puede cambiar cuando quiera.</summary>
public sealed partial class AppearanceService(IDbContextFactory<ApplicationDbContext> factory) : IAppearanceService
{
    private const string DoneKey = "dashboard.color.done";
    private const string InProgressKey = "dashboard.color.progress";
    private const string AlertKey = "dashboard.color.alert";

    private const string InstallationKey = "dashboard.chart.installation";
    private const string BrigadesKey = "dashboard.chart.brigades";
    private const string StagesKey = "dashboard.chart.stages";
    private const string NodesKey = "dashboard.chart.nodes";
    private const string StatesKey = "dashboard.chart.states";

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    public async Task<DashboardColors> GetColorsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith("dashboard.color.")).ToListAsync(ct);
        string Pick(string key, string fallback)
        {
            var value = rows.FirstOrDefault(r => r.Key == key)?.Value;
            return value is not null && HexColor().IsMatch(value) ? value.ToUpperInvariant() : fallback;
        }

        var d = DashboardColors.Default;
        return new DashboardColors(Pick(DoneKey, d.Done), Pick(InProgressKey, d.InProgress), Pick(AlertKey, d.Alert));
    }

    public async Task SaveColorsAsync(DashboardColors colors, CancellationToken ct = default)
    {
        foreach (var (name, value) in new[] { ("Finalizado", colors.Done), ("En avance", colors.InProgress), ("Alerta", colors.Alert) })
        {
            if (value is null || !HexColor().IsMatch(value))
            {
                throw new BusinessRuleException($"El color de «{name}» debe tener el formato #RRGGBB, por ejemplo #54A32E.");
            }
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        await Upsert(db, DoneKey, colors.Done.ToUpperInvariant(), ct);
        await Upsert(db, InProgressKey, colors.InProgress.ToUpperInvariant(), ct);
        await Upsert(db, AlertKey, colors.Alert.ToUpperInvariant(), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DashboardCharts> GetChartsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith("dashboard.chart.")).ToListAsync(ct);
        ChartKind Pick(string key, ChartKind fallback)
        {
            var value = rows.FirstOrDefault(r => r.Key == key)?.Value;
            return Enum.TryParse<ChartKind>(value, out var kind) && Enum.IsDefined(kind) ? kind : fallback;
        }

        var d = DashboardCharts.Default;
        return new DashboardCharts(Pick(InstallationKey, d.Installation), Pick(BrigadesKey, d.Brigades), Pick(StagesKey, d.Stages),
            Pick(NodesKey, d.Nodes), Pick(StatesKey, d.States));
    }

    public async Task SaveChartsAsync(DashboardCharts charts, CancellationToken ct = default)
    {
        foreach (var kind in new[] { charts.Installation, charts.Brigades, charts.Stages, charts.Nodes, charts.States })
        {
            if (!Enum.IsDefined(kind))
            {
                throw new BusinessRuleException("Ese tipo de gráfico no existe. Use barras, columnas, dona o pastel.");
            }
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        await Upsert(db, InstallationKey, charts.Installation.ToString(), ct);
        await Upsert(db, BrigadesKey, charts.Brigades.ToString(), ct);
        await Upsert(db, StagesKey, charts.Stages.ToString(), ct);
        await Upsert(db, NodesKey, charts.Nodes.ToString(), ct);
        await Upsert(db, StatesKey, charts.States.ToString(), ct);
        await db.SaveChangesAsync(ct);
    }

    public Task ResetChartsAsync(CancellationToken ct = default) => SaveChartsAsync(DashboardCharts.Default, ct);

    public Task ResetColorsAsync(CancellationToken ct = default) => SaveColorsAsync(DashboardColors.Default, ct);

    private static async Task Upsert(ApplicationDbContext db, string key, string value, CancellationToken ct)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
    }
}
