using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Application.Common;

/// <summary>Nombres en español de los enums técnicos, compartidos por la pantalla y los reportes.</summary>
public static class SpanishNames
{
    public static string Of(EquipmentKind kind) => kind switch
    {
        EquipmentKind.Olt => "OLT",
        EquipmentKind.Switch => "Switch",
        EquipmentKind.Router => "Router",
        _ => "Otro",
    };

    public static string Of(EquipmentStatus status) => status switch
    {
        EquipmentStatus.Planned => "Planificado",
        EquipmentStatus.Installed => "Instalado",
        EquipmentStatus.Operational => "En operación",
        EquipmentStatus.Faulty => "Con falla",
        EquipmentStatus.Retired => "Retirado",
        _ => status.ToString(),
    };

    public static string Of(TransceiverStatus status) => status switch
    {
        TransceiverStatus.InStock => "En stock",
        TransceiverStatus.Installed => "Instalado",
        TransceiverStatus.Faulty => "Con falla",
        TransceiverStatus.Replaced => "Reemplazado",
        _ => status.ToString(),
    };

    public static string Of(DocumentKind kind) => kind switch
    {
        DocumentKind.Atp => "ATP",
        DocumentKind.Caes => "CAES",
        DocumentKind.Minute => "Minuta",
        DocumentKind.Photo => "Foto",
        DocumentKind.Annex => "Anexo",
        _ => "Otro",
    };

    public static string Of(StageStatus status) => status switch
    {
        StageStatus.Pending => "Pendiente",
        StageStatus.InProgress => "En proceso",
        StageStatus.Finished => "Finalizado",
        StageStatus.InCorrection => "En corrección",
        StageStatus.NotApplicable => "N/A",
        _ => status.ToString(),
    };
}
