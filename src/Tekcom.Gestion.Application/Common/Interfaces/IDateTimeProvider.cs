namespace Tekcom.Gestion.Application.Common.Interfaces;

/// <summary>
/// Abstrae la hora actual para que las reglas de negocio (ritmo, vencimientos,
/// auditoría) se puedan probar con fechas fijas.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
