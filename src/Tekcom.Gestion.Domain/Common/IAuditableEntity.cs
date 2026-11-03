namespace Tekcom.Gestion.Domain.Common;

/// <summary>
/// Entidad que registra quién y cuándo la creó o modificó.
/// Los valores los asigna automáticamente la infraestructura al guardar.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
