namespace Tekcom.Gestion.Domain.Enums;

public enum ProjectStatus
{
    Planning = 0,
    Active = 1,
    OnHold = 2,
    Closed = 3,
}

/// <summary>Módulos que un tipo de proyecto tiene activos (principio 8 del plan).</summary>
[Flags]
public enum ProjectModules
{
    None = 0,
    WorkPlan = 1,
    Sprints = 2,
    Tasks = 4,
    Documents = 8,
    Sites = 16,
    Warehouse = 32,
    Projection = 64,
    Liquidation = 128,
    Equipment = 256,
    Feeder = 512,

    /// <summary>Proyectos generales: solo plan, sprints, tareas y documentos.</summary>
    General = WorkPlan | Sprints | Tasks | Documents,

    /// <summary>Proyectos FTTH/telecom: todo.</summary>
    All = WorkPlan | Sprints | Tasks | Documents | Sites | Warehouse | Projection | Liquidation | Equipment | Feeder,
}

public enum MaterialCategory
{
    FiberCable = 0,     // Cable FC preconectorizado
    MpoCable = 1,
    NetworkElement = 2, // Sub-box, end-box, Hub-box, X-box
    Hardware = 3,       // Herrajes, abrazaderas, pernos, etc.
    Consumable = 4,
    FeederCable = 5,
    Closure = 6,
    Odf = 7,
    Crossing = 8,       // Cruces americanos (partida contada, no pasa por bodega)
    Activity = 9,       // Fusiones y otras actividades contadas
    Other = 10,
}

/// <summary>Estado de un documento de cierre del nodo (KMZ, consolidado de UM, reporte fotográfico).</summary>
public enum ClosureDocStatus
{
    PendingContractor = 0,  // "PENDIENTE POR CONTRATISTA"
    Validated = 1,          // "VALIDADA"
}

public enum MovementType
{
    Receipt = 0,    // Ingreso
    Delivery = 1,   // Entrega a contratista
    Return = 2,     // Devolución
    Adjustment = 3, // Ajuste con motivo (suma o resta)
    Installed = 4,  // Instalado en campo
}

/// <summary>Etapas del nodo. Ver docs/FLUJO_OPERATIVO.md.</summary>
public enum StageCode
{
    Caes = 0,
    Layout = 1,
    MaterialDelivery = 2,
    FcCable = 3,
    MpoCable = 4,
    Fat = 5,
    HubAndXBox = 6,
    Fusion = 7,
    AmericanCrossings = 8,
    FatMeasurements = 9,
    Corrections = 10,
    Atp = 11,
    FinalReception = 12,
}

public enum StageStatus
{
    Pending = 0,
    InProgress = 1,
    Finished = 2,
    InCorrection = 3,
    NotApplicable = 4,
}

/// <summary>Estado manual del feeder en la ODN de un nodo.</summary>
public enum FeederStatus
{
    Pending = 0,
    InProgress = 1,
    Finished = 2,
}

public enum FeederItemKind
{
    Cable12 = 0,
    Cable24 = 1,
    Cable48 = 2,
    Cable96 = 3,
    Closure12 = 4,
    Closure24 = 5,
    Closure48 = 6,
    Closure96 = 7,
    Odf96 = 8,
}

/// <summary>
/// Rol de un material dentro del flujo del nodo. Permite calcular el avance de cada etapa
/// (cables, FATs, fusiones, cruces) sin depender del nombre del material.
/// </summary>
public enum QuantityGroup
{
    None = 0,
    FcCable = 1,
    MpoCable = 2,
    SubBox = 3,
    EndBox = 4,
    HubBox = 5,
    XBox = 6,
    Fusion = 7,
    Crossing = 8,
}

public enum PaceStatus
{
    Unknown = 0,
    Ahead = 1,
    OnTime = 2,
    Behind = 3,
    Completed = 4,
}

public enum SprintStatus
{
    Planned = 0,
    Active = 1,
    Closed = 2,
}

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>Columnas del tablero Kanban.</summary>
public enum TaskItemStatus
{
    ToDo = 0,
    InProgress = 1,
    InReview = 2,
    Done = 3,
}

public enum LiquidationStatus
{
    Open = 0,
    Closed = 1,
}

public enum StockStatus
{
    Ok = 0,
    Low = 1,
    Shortage = 2,
    Negative = 3,
}

public enum NotificationType
{
    TaskAssigned = 0,
    TaskDueSoon = 1,
    TaskOverdue = 2,
    SprintBehind = 3,
    ProjectBehind = 4,
    LowStock = 5,
    DailySummary = 6,
}

public enum NotificationChannelKind
{
    Email = 0,
    WhatsApp = 1,
}

public enum NotificationStatus
{
    Sent = 0,
    Failed = 1,
}

public enum EquipmentKind
{
    Olt = 0,
    Switch = 1,
    Router = 2,
    Other = 9,
}

public enum EquipmentStatus
{
    Planned = 0,
    Installed = 1,
    Operational = 2,
    Faulty = 3,
    Retired = 4,
}

public enum TransceiverStatus
{
    InStock = 0,
    Installed = 1,
    Faulty = 2,
    Replaced = 3,
}

public enum DocumentKind
{
    Atp = 0,
    Caes = 1,
    Minute = 2,
    Photo = 3,
    Annex = 4,
    Other = 9,
}
