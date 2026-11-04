using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>Equipo activo del proyecto (OLT, switch, etc.), opcionalmente ubicado en un nodo.</summary>
public class Equipment : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public EquipmentKind Kind { get; set; } = EquipmentKind.Olt;
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Planned;
    public string? Notes { get; set; }

    public ICollection<EquipmentSlot> Slots { get; set; } = [];
}

/// <summary>Tarjeta instalada en una ranura (slot) del equipo.</summary>
public class EquipmentSlot : AuditableEntity
{
    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public int SlotNumber { get; set; }
    public string? CardModel { get; set; }
    public string? CardSerial { get; set; }

    /// <summary>Cantidad de puertos de la tarjeta.</summary>
    public int PortCount { get; set; }
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Installed;
    public string? Notes { get; set; }
}

/// <summary>Transceptor (SFP) con serial. Puede estar en un puerto de una tarjeta o en stock.</summary>
public class Transceiver : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public int? SlotId { get; set; }
    public EquipmentSlot? Slot { get; set; }
    public int? PortNumber { get; set; }

    public string? Model { get; set; }
    public string Serial { get; set; } = string.Empty;
    public int? WavelengthNm { get; set; }
    public TransceiverStatus Status { get; set; } = TransceiverStatus.InStock;
    public DateOnly? InstalledOn { get; set; }
    public string? Notes { get; set; }
}
