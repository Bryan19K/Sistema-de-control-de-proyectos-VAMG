namespace Tekcom.Gestion.Application.Common.Interfaces;

/// <summary>
/// Expone el usuario autenticado que ejecuta la operación actual.
/// Lo usa el interceptor de auditoría para llenar CreatedBy / UpdatedBy.
/// </summary>
public interface ICurrentUserService
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
}
