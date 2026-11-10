using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>Ajuste del sistema en forma de clave y valor (por ejemplo, los colores del dashboard).</summary>
public class AppSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
