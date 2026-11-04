using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Web.Components.Shared;

namespace Tekcom.Gestion.Web.Services;

/// <summary>Textos en español de los enums y su apariencia (chips) en pantalla.</summary>
public static class UiText
{
    public static string StageStatusName(StageStatus status) => status switch
    {
        StageStatus.Pending => "Pendiente",
        StageStatus.InProgress => "En proceso",
        StageStatus.Finished => "Finalizado",
        StageStatus.InCorrection => "En corrección",
        StageStatus.NotApplicable => "N/A",
        _ => status.ToString(),
    };

    public static StatusChip.Kind StageStatusKind(StageStatus status) => status switch
    {
        StageStatus.Finished => StatusChip.Kind.Ok,
        StageStatus.InProgress => StatusChip.Kind.EnTiempo,
        StageStatus.InCorrection => StatusChip.Kind.Bajo,
        _ => StatusChip.Kind.Neutral,
    };

    public static string FeederStatusName(FeederStatus status) => status switch
    {
        FeederStatus.Pending => "Pendiente",
        FeederStatus.InProgress => "En proceso",
        FeederStatus.Finished => "Finalizado",
        _ => status.ToString(),
    };

    public static StatusChip.Kind FeederStatusKind(FeederStatus status) => status switch
    {
        FeederStatus.Finished => StatusChip.Kind.Ok,
        FeederStatus.InProgress => StatusChip.Kind.EnTiempo,
        _ => StatusChip.Kind.Neutral,
    };

    public static string ProjectStatusName(ProjectStatus status) => status switch
    {
        ProjectStatus.Planning => "En planificación",
        ProjectStatus.Active => "Activo",
        ProjectStatus.OnHold => "En pausa",
        ProjectStatus.Closed => "Cerrado",
        _ => status.ToString(),
    };

    public static StatusChip.Kind ProjectStatusKind(ProjectStatus status) => status switch
    {
        ProjectStatus.Active => StatusChip.Kind.EnTiempo,
        ProjectStatus.OnHold => StatusChip.Kind.Bajo,
        ProjectStatus.Closed => StatusChip.Kind.Ok,
        _ => StatusChip.Kind.Neutral,
    };

    public static (StatusChip.Kind Kind, string Text) Pace(PaceStatus status) => status switch
    {
        PaceStatus.Ahead => (StatusChip.Kind.Adelantado, "Adelantado"),
        PaceStatus.OnTime => (StatusChip.Kind.EnTiempo, "En tiempo"),
        PaceStatus.Behind => (StatusChip.Kind.Atrasado, "Atrasado"),
        PaceStatus.Completed => (StatusChip.Kind.Ok, "Finalizado"),
        _ => (StatusChip.Kind.Neutral, "Sin fechas"),
    };

    public static string CategoryName(MaterialCategory category) => category switch
    {
        MaterialCategory.FiberCable => "Cable FC",
        MaterialCategory.MpoCable => "Cable MPO",
        MaterialCategory.NetworkElement => "Elementos de red",
        MaterialCategory.Hardware => "Herrajes y accesorios",
        MaterialCategory.Consumable => "Consumibles",
        MaterialCategory.FeederCable => "Cable de feeder",
        MaterialCategory.Closure => "Cierres",
        MaterialCategory.Odf => "ODF",
        MaterialCategory.Crossing => "Cruces americanos",
        MaterialCategory.Activity => "Actividades",
        _ => "Otros",
    };

    public static string GroupName(QuantityGroup group) => group switch
    {
        QuantityGroup.FcCable => "Cable FC",
        QuantityGroup.MpoCable => "Cable MPO",
        QuantityGroup.SubBox => "Sub-box (NAP de paso)",
        QuantityGroup.EndBox => "End-box (NAP final)",
        QuantityGroup.HubBox => "Hub-box",
        QuantityGroup.XBox => "X-box",
        QuantityGroup.Fusion => "Fusiones",
        QuantityGroup.Crossing => "Cruces americanos",
        _ => "Ninguno",
    };

    public static string StageName(StageCode code) => code switch
    {
        StageCode.Caes => "Supervisión de CAES",
        StageCode.Layout => "Replanteo",
        StageCode.MaterialDelivery => "Entrega de materiales",
        StageCode.FcCable => "Cable FC",
        StageCode.MpoCable => "Cable MPO",
        StageCode.Fat => "FAT",
        StageCode.HubAndXBox => "Hub-box y X-box",
        StageCode.Fusion => "Fusiones",
        StageCode.AmericanCrossings => "Cruces americanos",
        StageCode.FatMeasurements => "Mediciones",
        StageCode.Corrections => "Correcciones",
        StageCode.Atp => "ATP",
        StageCode.FinalReception => "Recepción final",
        _ => code.ToString(),
    };

    public static string RoleName(string role) => role switch
    {
        "Admin" => "Administrador",
        "Bodega" => "Bodega",
        "Supervisor" => "Supervisor",
        "Gerencia" => "Gerencia (solo lectura)",
        "Contratista" => "Contratista",
        _ => role,
    };

    public static string Number(decimal value) =>
        value == Math.Floor(value)
            ? value.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("es-SV"))
            : value.ToString("#,##0.###", System.Globalization.CultureInfo.GetCultureInfo("es-SV"));

    public static string Date(DateOnly? date) => date is null ? "—" : date.Value.ToString("dd/MM/yyyy");

    public static string ShortDate(DateOnly? date) => date is null ? "—" : date.Value.ToString("dd/MM/yy");

    public static string TaskStatusName(TaskItemStatus status) => status switch
    {
        TaskItemStatus.ToDo => "Por hacer",
        TaskItemStatus.InProgress => "En proceso",
        TaskItemStatus.InReview => "En revisión",
        TaskItemStatus.Done => "Hecho",
        _ => status.ToString(),
    };

    public static string PriorityName(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "Baja",
        TaskPriority.Medium => "Media",
        TaskPriority.High => "Alta",
        _ => priority.ToString(),
    };

    public static string SprintStatusName(SprintStatus status) => status switch
    {
        SprintStatus.Planned => "Planificado",
        SprintStatus.Active => "Activo",
        SprintStatus.Closed => "Cerrado",
        _ => status.ToString(),
    };

    public static StatusChip.Kind SprintStatusKind(SprintStatus status) => status switch
    {
        SprintStatus.Active => StatusChip.Kind.EnTiempo,
        SprintStatus.Closed => StatusChip.Kind.Ok,
        _ => StatusChip.Kind.Neutral,
    };

    private static readonly string[] Days = ["Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb"];

    /// <summary>Etiqueta de la fecha límite: Hoy, Mañana, Vencida, día de la semana o fecha.</summary>
    public static (StatusChip.Kind Kind, string Text)? Due(DateOnly? due, DateOnly today, bool done)
    {
        if (due is null)
        {
            return null;
        }

        if (done)
        {
            return (StatusChip.Kind.Neutral, ShortDate(due));
        }

        var diff = due.Value.DayNumber - today.DayNumber;
        return diff switch
        {
            < 0 => (StatusChip.Kind.Vencida, "Vencida"),
            0 => (StatusChip.Kind.Hoy, "Hoy"),
            1 => (StatusChip.Kind.Manana, "Mañana"),
            <= 6 => (StatusChip.Kind.Dia, Days[(int)due.Value.DayOfWeek]),
            _ => (StatusChip.Kind.Neutral, due.Value.ToString("dd/MM")),
        };
    }
}
