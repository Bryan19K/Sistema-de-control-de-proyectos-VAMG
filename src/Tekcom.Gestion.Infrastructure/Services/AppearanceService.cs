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
