using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

public class Project : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    public int ProjectTypeId { get; set; }
    public ProjectType? ProjectType { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public string? ResponsibleName { get; set; }

    /// <summary>Margen del indicador de ritmo, en puntos porcentuales (10 por defecto).</summary>
    public int PaceMarginPercent { get; set; } = 10;

    /// <summary>Porcentaje de FATs instaladas a partir del cual un nodo está "listo para medir".</summary>
    public int ReadyToMeasurePercent { get; set; } = 90;

    /// <summary>Rango aceptable de potencia (dBm). Si ambos se definen, la medición se califica sola.</summary>
    public decimal? MinAcceptableDbm { get; set; }
    public decimal? MaxAcceptableDbm { get; set; }

    public ICollection<Site> Sites { get; set; } = [];
}

/// <summary>Nodo o sitio de un proyecto (ej. SON19).</summary>
public class Site : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Code { get; set; } = string.Empty;
    public string? Zone { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public int? ContractorId { get; set; }
    public Contractor? Contractor { get; set; }

    public int? SupervisorId { get; set; }
    public Supervisor? Supervisor { get; set; }

    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }

    /// <summary>La supervisión de CAES es solo informativa: no afecta el flujo.</summary>
    public bool CaesSupervised { get; set; }

    /// <summary>"ODN finalizada": se marca a mano.</summary>
    public bool OdnFinalized { get; set; }
    public DateOnly? OdnFinalizedOn { get; set; }

    /// <summary>Estado del feeder en este nodo: solo indica si ya pasó por la ODN.</summary>
    public FeederStatus FeederStatus { get; set; } = FeederStatus.Pending;

    /// <summary>Ya se tomó la información de las bitácoras de los supervisores ("ACTUALIZADO" del Excel).</summary>
    public bool LogsUpToDate { get; set; }
    public DateOnly? LogsUpdatedOn { get; set; }

    public string? Notes { get; set; }

    public ICollection<SiteStage> Stages { get; set; } = [];

    /// <summary>Un nodo está listo para entregar al cliente con ODN finalizada y feeder finalizado.</summary>
    public bool IsReadyToDeliver => OdnFinalized && FeederStatus == FeederStatus.Finished;

    public string? MapUrl => Latitude is null || Longitude is null
        ? null
        : FormattableString.Invariant($"https://www.google.com/maps?q={Latitude},{Longitude}");
}

/// <summary>Definición de una etapa para un tipo de proyecto (plantilla con días configurables).</summary>
public class StageDefinition : AuditableEntity
{
    public int ProjectTypeId { get; set; }
    public ProjectType? ProjectType { get; set; }

    public StageCode Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>Días planificados. El avance del nodo se pondera por estos días.</summary>
    public decimal PlannedDays { get; set; } = 1;

    /// <summary>Las etapas informativas (CAES) no cuentan para el avance.</summary>
    public bool CountsForProgress { get; set; } = true;
}

public class SiteStage : AuditableEntity
{
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    public int StageDefinitionId { get; set; }
    public StageDefinition? Definition { get; set; }

    public StageCode Code { get; set; }
    public StageStatus Status { get; set; } = StageStatus.Pending;

    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public string? Notes { get; set; }
}
