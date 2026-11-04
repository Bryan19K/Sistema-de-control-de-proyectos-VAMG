using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>Regla de aviso: qué eventos se notifican y por qué canales. Hay una por tipo de aviso.</summary>
public class NotificationRule : AuditableEntity
{
    public NotificationType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public bool SendEmail { get; set; } = true;
    public bool SendWhatsApp { get; set; } = true;
}

/// <summary>Bitácora de avisos enviados (o que fallaron). Nunca se borra.</summary>
public class NotificationLog : AuditableEntity
{
    public NotificationType Type { get; set; }
    public NotificationChannelKind Channel { get; set; }
    public NotificationStatus Status { get; set; }

    public string? UserId { get; set; }

    /// <summary>Correo o número de WhatsApp al que se envió.</summary>
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Error { get; set; }

    /// <summary>Clave para no repetir el mismo aviso al mismo destinatario el mismo día.</summary>
    public string DedupeKey { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
}
