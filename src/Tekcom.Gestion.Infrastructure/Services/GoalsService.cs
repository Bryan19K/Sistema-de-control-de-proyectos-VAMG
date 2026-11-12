using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>Metas de construcción que el administrador puede cambiar (por ahora, los home passes por semana).</summary>
public sealed class GoalsService(IDbContextFactory<ApplicationDbContext> factory) : IGoalsService
{
    private const string WeeklyHpKey = "goal.weeklyHp";

    public async Task<int> GetWeeklyHpGoalAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var value = (await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == WeeklyHpKey, ct))?.Value;
        return int.TryParse(value, out var goal) && goal > 0 ? goal : IGoalsService.DefaultWeeklyHp;
    }

    public async Task SaveWeeklyHpGoalAsync(int homePassesPerWeek, CancellationToken ct = default)
    {
        if (homePassesPerWeek is < 1 or > 1_000_000)
        {
            throw new BusinessRuleException("La meta semanal debe ser un número de home passes mayor que cero.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == WeeklyHpKey, ct);
        if (row is null) { db.AppSettings.Add(new AppSetting { Key = WeeklyHpKey, Value = homePassesPerWeek.ToString() }); }
        else { row.Value = homePassesPerWeek.ToString(); }
        await db.SaveChangesAsync(ct);
    }
}
