using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class MeasurementService(IDbContextFactory<ApplicationDbContext> factory) : IMeasurementService
{
    public async Task<List<FatMeasurement>> ListAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.FatMeasurements.AsNoTracking()
            .Where(m => m.SiteId == siteId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .ToListAsync(ct);
    }

    public async Task<FatMeasurement> UpdateAsync(int measurementId, string fatCode, DateOnly date, decimal powerDbm, bool? isAcceptable, string? notes,
        CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var measurement = await db.FatMeasurements.FirstOrDefaultAsync(m => m.Id == measurementId, ct)
            ?? throw new BusinessRuleException("La medición no existe.");
        var siteId = measurement.SiteId;
        fatCode = (fatCode ?? string.Empty).Trim().ToUpperInvariant();
        if (fatCode.Length == 0)
        {
            throw new BusinessRuleException("El código de la FAT es obligatorio.");
        }

        if (powerDbm is > 10 or < -60)
        {
            throw new BusinessRuleException("La potencia debe estar entre -60 y 10 dBm.");
        }

        var site = await db.Sites.AsNoTracking().FirstAsync(s => s.Id == siteId, ct);
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == site.ProjectId, ct);
        bool acceptable;
        if (project.MinAcceptableDbm is { } min && project.MaxAcceptableDbm is { } max)
        {
            acceptable = powerDbm >= min && powerDbm <= max;
        }
        else
        {
            acceptable = isAcceptable ?? throw new BusinessRuleException("Indique si la medición es aceptable.");
        }

        measurement.FatCode = fatCode;
        measurement.Date = date;
        measurement.PowerDbm = powerDbm;
        measurement.IsAcceptable = acceptable;
        measurement.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await db.SaveChangesAsync(ct);
        return measurement;
    }

    public async Task<FatMeasurement> AddAsync(int siteId, string fatCode, DateOnly date, decimal powerDbm, bool? isAcceptable,
        string? notes, CancellationToken ct = default)
    {
        fatCode = (fatCode ?? string.Empty).Trim().ToUpperInvariant();
        if (fatCode.Length == 0)
        {
            throw new BusinessRuleException("El código de la FAT es obligatorio.");
        }

        if (powerDbm is > 10 or < -60)
        {
            throw new BusinessRuleException("La potencia debe estar entre -60 y 10 dBm.");
        }

        await using var db = factory.CreateDbContext();
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new BusinessRuleException("El nodo no existe.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == site.ProjectId, ct);

        bool acceptable;
        if (project.MinAcceptableDbm is { } min && project.MaxAcceptableDbm is { } max)
        {
            // Con el rango definido en el proyecto la medición se califica sola.
            acceptable = powerDbm >= min && powerDbm <= max;
        }
        else
        {
            acceptable = isAcceptable
                ?? throw new BusinessRuleException("Indique si la medición es aceptable (el proyecto no define el rango de dBm).");
        }

        var measurement = new FatMeasurement
        {
            ProjectId = site.ProjectId, SiteId = siteId, FatCode = fatCode, Date = date, PowerDbm = powerDbm,
            IsAcceptable = acceptable, Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        db.FatMeasurements.Add(measurement);
        await db.SaveChangesAsync(ct);
        return measurement;
    }

    public async Task RemoveAsync(int measurementId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var measurement = await db.FatMeasurements.FirstOrDefaultAsync(m => m.Id == measurementId, ct);
        if (measurement is not null)
        {
            db.FatMeasurements.Remove(measurement);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<MeasurementSummary> SummaryAsync(int siteId, CancellationToken ct = default)
    {
        var all = await ListAsync(siteId, ct);
        var latest = all.GroupBy(m => m.FatCode).Select(g => g.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).First()).ToList();
        return new MeasurementSummary(latest.Count, latest.Count(m => m.IsAcceptable), latest.Count(m => !m.IsAcceptable));
    }
}
