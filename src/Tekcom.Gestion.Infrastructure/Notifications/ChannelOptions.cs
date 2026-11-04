namespace Tekcom.Gestion.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "TEK-COM Gestión";

    /// <summary>true = SSL directo (puerto 465); false = STARTTLS si el servidor lo ofrece (puerto 587).</summary>
    public bool UseSsl { get; set; }
}

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>Id del número de teléfono en WhatsApp Business Cloud API.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>Token de acceso permanente (usuario del sistema). Va en user-secrets o variables de entorno, nunca en el repositorio.</summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Nombre de la plantilla aprobada por Meta, con un solo parámetro de cuerpo ({{1}}) para el texto del aviso.
    /// Sin plantilla solo se pueden enviar mensajes de texto libre dentro de las 24 h de una conversación abierta por el usuario.
    /// </summary>
    public string? TemplateName { get; set; }
    public string LanguageCode { get; set; } = "es";
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>Código de país que se antepone a números de 8 dígitos (El Salvador: 503).</summary>
    public string DefaultCountryCode { get; set; } = "503";
}
