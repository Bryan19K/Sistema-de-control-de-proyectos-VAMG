namespace Tekcom.Gestion.Domain.Common;

/// <summary>
/// Clase base para todas las entidades del sistema: identificador, campos de
/// auditoría y borrado lógico (principios 3 y 4 del plan).
/// </summary>
public abstract class AuditableEntity : IAuditableEntity, ISoftDeletable
{
    public int Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
