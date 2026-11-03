namespace Tekcom.Gestion.Domain.Common;

/// <summary>
/// Entidad que nunca se borra físicamente. Al "eliminar" se marca
/// <see cref="IsDeleted"/> y se conserva el registro para auditoría.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
