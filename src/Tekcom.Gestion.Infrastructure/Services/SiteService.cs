using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class SiteService(
    IDbContextFactory<ApplicationDbContext> factory,
    IQuantityService quantities,
    IDateTimeProvider clock) : ISiteService
{
    private static readonly QuantityGroup[] FatGroups =
        [QuantityGroup.SubBox, QuantityGroup.EndBox, QuantityGroup.HubBox, QuantityGroup.XBox];

    public async Task<List<SiteSummary>> ListAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new BusinessRuleException("El proyecto no existe.");

        var sites = await db.Sites.AsNoTracking()
            .Include(s => s.Contractor)
            .Include(s => s.Supervisor)
            .Include(s => s.Stages).ThenInclude(st => st.Definition)
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Code)
            .ToListAsync(ct);

        var totals = await quantities.GetGroupTotalsBySiteAsync(projectId, ct);
        return sites.Select(s => BuildSummary(s, project, totals.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<SiteSummary?> GetSummaryAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var site = await db.Sites.AsNoTracking()
            .Include(s => s.Contractor)
            .Include(s => s.Supervisor)
            .Include(s => s.Stages).ThenInclude(st => st.Definition)
            .FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site is null)
        {
            return null;
        }

        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == site.ProjectId, ct);
        var totals = await quantities.GetGroupTotalsBySiteAsync(site.ProjectId, ct);
        return BuildSummary(site, project, totals.GetValueOrDefault(site.Id));
    }

    public async Task<Site?> GetAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.Sites.AsNoTracking()
            .Include(s => s.Contractor)
            .Include(s => s.Supervisor)
            .FirstOrDefaultAsync(s => s.Id == siteId, ct);
    }

    public async Task<Site> SaveAsync(Site site, CancellationToken ct = default)
    {
        var code = (site.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            throw new BusinessRuleException("El código del nodo es obligatorio.");
        }

        if (site.PlannedStart is not null && site.PlannedEnd is not null && site.PlannedEnd < site.PlannedStart)
        {
            throw new BusinessRuleException("La fecha final no puede ser anterior a la inicial.");
        }

        if (site.Latitude is < -90 or > 90 || site.Longitude is < -180 or > 180)
        {
            throw new BusinessRuleException("Las coordenadas no son válidas.");
        }

        await using var db = factory.CreateDbContext();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == site.ProjectId, ct)
            ?? throw new BusinessRuleException("El proyecto no existe.");

        if (await db.Sites.AnyAsync(s => s.ProjectId == site.ProjectId && s.Id != site.Id && s.Code == code, ct))
        {
            throw new BusinessRuleException($"Ya existe el nodo '{code}' en este proyecto.");
        }

        var isNew = site.Id == 0;
        var entity = isNew
            ? db.Sites.Add(new Site { ProjectId = site.ProjectId }).Entity
            : await db.Sites.FirstAsync(s => s.Id == site.Id, ct);

        entity.Code = code;
        entity.Zone = string.IsNullOrWhiteSpace(site.Zone) ? null : site.Zone.Trim();
        entity.Latitude = site.Latitude;
        entity.Longitude = site.Longitude;
        entity.ContractorId = site.ContractorId;
        entity.SupervisorId = site.SupervisorId;
        entity.PlannedStart = site.PlannedStart;
        entity.PlannedEnd = site.PlannedEnd;
        entity.ActualStart = site.ActualStart;
        entity.ActualEnd = site.ActualEnd;
        entity.Notes = string.IsNullOrWhiteSpace(site.Notes) ? null : site.Notes.Trim();
        entity.HomesPassed = site.HomesPassed is < 0 ? null : site.HomesPassed;
        entity.ElectricalPermitStatus = string.IsNullOrWhiteSpace(site.ElectricalPermitStatus) ? null : site.ElectricalPermitStatus.Trim();
        entity.MaterialsDelivered = site.MaterialsDelivered;
        entity.KmzStatus = site.KmzStatus;
        entity.UmConsolidatedStatus = site.UmConsolidatedStatus;
        entity.PhotoReportStatus = site.PhotoReportStatus;
        entity.AtpDelivered = site.AtpDelivered;

        await db.SaveChangesAsync(ct);

        if (isNew)
        {
            await EnsureStagesAsync(db, entity, project.ProjectTypeId, ct);
        }

        return entity;
    }

    public async Task<List<SiteStage>> StagesAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new BusinessRuleException("El nodo no existe.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == site.ProjectId, ct);

        // Si se agregaron etapas a la plantilla después de crear el nodo, se completan aquí.
        await EnsureStagesAsync(db, site, project.ProjectTypeId, ct);

        return await db.SiteStages.AsNoTracking()
            .Include(st => st.Definition)
            .Where(st => st.SiteId == siteId)
            .OrderBy(st => st.Definition!.SortOrder)
            .ToListAsync(ct);
    }

    public async Task SetStageAsync(int siteId, StageCode code, StageStatus status, DateOnly? date, string? notes, CancellationToken ct = default)
    {
        if (status is StageStatus.InCorrection or StageStatus.NotApplicable && code != StageCode.Corrections)
        {
            throw new BusinessRuleException("Los estados 'En corrección' y 'N/A' solo aplican a las correcciones estéticas y técnicas.");
        }

        await using var db = factory.CreateDbContext();
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new BusinessRuleException("El nodo no existe.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == site.ProjectId, ct);
        await EnsureStagesAsync(db, site, project.ProjectTypeId, ct);

        var stage = await db.SiteStages.FirstOrDefaultAsync(st => st.SiteId == siteId && st.Code == code, ct)
            ?? throw new BusinessRuleException("La etapa no existe para este tipo de proyecto.");

        var day = date ?? clock.Today;
        stage.Status = status;
        stage.Notes = string.IsNullOrWhiteSpace(notes) ? stage.Notes : notes.Trim();

        switch (status)
        {
            case StageStatus.Pending:
                stage.ActualStart = null;
                stage.ActualEnd = null;
                break;
            case StageStatus.InProgress:
                stage.ActualStart ??= day;
                stage.ActualEnd = null;
                break;
            case StageStatus.Finished:
                stage.ActualStart ??= day;
                stage.ActualEnd = day;
                break;
        }

        if (status is StageStatus.InProgress or StageStatus.Finished)
        {
            site.ActualStart ??= day;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetStageDatesAsync(int siteId, StageCode code, DateOnly? plannedStart, DateOnly? plannedEnd, DateOnly? actualStart, DateOnly? actualEnd,
        CancellationToken ct = default)
    {
        if (plannedStart is not null && plannedEnd is not null && plannedEnd < plannedStart)
        {
            throw new BusinessRuleException("La fecha final planificada no puede ser anterior a la inicial.");
        }

        if (actualStart is not null && actualEnd is not null && actualEnd < actualStart)
        {
            throw new BusinessRuleException("La fecha final real no puede ser anterior a la inicial.");
        }

        await using var db = factory.CreateDbContext();
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct) ?? throw new BusinessRuleException("El nodo no existe.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == site.ProjectId, ct);
        await EnsureStagesAsync(db, site, project.ProjectTypeId, ct);
        var stage = await db.SiteStages.FirstOrDefaultAsync(st => st.SiteId == siteId && st.Code == code, ct)
            ?? throw new BusinessRuleException("La etapa no existe para este tipo de proyecto.");
        stage.PlannedStart = plannedStart;
        stage.PlannedEnd = plannedEnd;
        stage.ActualStart = actualStart;
        stage.ActualEnd = actualEnd;
        await db.SaveChangesAsync(ct);
    }

    public Task SetOdnFinalizedAsync(int siteId, bool finalized, CancellationToken ct = default) =>
        UpdateAsync(siteId, s =>
        {
            s.OdnFinalized = finalized;
            s.OdnFinalizedOn = finalized ? clock.Today : null;
            s.ActualEnd = finalized ? clock.Today : null;
        }, ct);

    public Task SetFeederStatusAsync(int siteId, FeederStatus status, CancellationToken ct = default) =>
        UpdateAsync(siteId, s => s.FeederStatus = status, ct);

    public Task SetCaesAsync(int siteId, bool supervised, CancellationToken ct = default) =>
        UpdateAsync(siteId, s => s.CaesSupervised = supervised, ct);

    public Task SetLogsUpToDateAsync(int siteId, bool upToDate, CancellationToken ct = default) =>
        UpdateAsync(siteId, s =>
        {
            s.LogsUpToDate = upToDate;
            s.LogsUpdatedOn = upToDate ? clock.Today : null;
        }, ct);

    private async Task UpdateAsync(int siteId, Action<Site> change, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct)
            ?? throw new BusinessRuleException("El nodo no existe.");
        change(site);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Crea las etapas que faltan según la plantilla del tipo de proyecto.</summary>
    public static async Task EnsureStagesAsync(ApplicationDbContext db, Site site, int projectTypeId, CancellationToken ct)
    {
        var definitions = await db.StageDefinitions.Where(d => d.ProjectTypeId == projectTypeId).ToListAsync(ct);
        if (definitions.Count == 0)
        {
            return;
        }

        var existing = await db.SiteStages.Where(st => st.SiteId == site.Id).Select(st => st.Code).ToListAsync(ct);
        var added = false;
        foreach (var definition in definitions.Where(d => !existing.Contains(d.Code)))
        {
            db.SiteStages.Add(new SiteStage
            {
                SiteId = site.Id,
                StageDefinitionId = definition.Id,
                Code = definition.Code,
                Status = StageStatus.Pending,
            });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private SiteSummary BuildSummary(Site site, Project project, Dictionary<QuantityGroup, QuantityPair>? totals)
    {
        totals ??= [];
        var progress = StageProgressCalculator.Percent(site.Stages.Select(st =>
            new StageWeight(st.Status, st.Definition?.PlannedDays ?? 1, st.Definition?.CountsForProgress ?? true)));

        var fatProjected = FatGroups.Sum(g => totals.GetValueOrDefault(g).Projected);
        var fatInstalled = FatGroups.Sum(g => totals.GetValueOrDefault(g).Installed);
        var fatPercent = fatProjected <= 0 ? 0 : Math.Round(fatInstalled / fatProjected * 100m, 1);

        QuantityGroup[] pieceGroups = [QuantityGroup.FcCable, QuantityGroup.MpoCable, QuantityGroup.SubBox, QuantityGroup.EndBox,
            QuantityGroup.HubBox, QuantityGroup.XBox, QuantityGroup.Fusion, QuantityGroup.Crossing];
        var piecesProjected = pieceGroups.Sum(g => totals.GetValueOrDefault(g).Projected);
        var piecesDone = pieceGroups.Sum(g => Math.Min(totals.GetValueOrDefault(g).Installed, totals.GetValueOrDefault(g).Projected));
        decimal? piecesPercent = piecesProjected <= 0 ? null : Math.Round(piecesDone / piecesProjected * 100m, 1);

        // El ritmo compara el avance global (piezas instaladas sobre lo proyectado) contra el tiempo transcurrido;
        // si el nodo no tiene proyección, usa el avance por etapas.
        var pace = PaceCalculator.Calculate(
            site.OdnFinalized ? 100 : piecesPercent ?? progress,
            site.PlannedStart,
            site.PlannedEnd,
            clock.Today,
            project.PaceMarginPercent);

        var stages = site.Stages.ToDictionary(st => st.Code, st => st.Status);
        var measured = stages.GetValueOrDefault(StageCode.FatMeasurements) == StageStatus.Finished;

        return new SiteSummary
        {
            Site = site,
            ProgressPercent = progress,
            Pace = pace,
            Stages = stages,
            Quantities = totals,
            FatPercent = fatPercent,
            PiecesPercent = piecesPercent,
            ReadyToMeasure = fatProjected > 0 && fatPercent >= project.ReadyToMeasurePercent && !measured,
        };
    }
}
