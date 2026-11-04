using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Tekcom.Gestion.Application.Notifications;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Infrastructure.Notifications;

/// <summary>WhatsApp Business Cloud API (Meta). Desactivado mientras no se configure el token y el número.</summary>
public sealed partial class WhatsAppChannel(HttpClient http, IOptions<WhatsAppOptions> options) : INotificationChannel
{
    private readonly WhatsAppOptions _options = options.Value;

    public NotificationChannelKind Kind => NotificationChannelKind.WhatsApp;

    public bool IsConfigured => MissingSetup is null;

    public string? MissingSetup =>
        string.IsNullOrWhiteSpace(_options.PhoneNumberId) ? "Falta WhatsApp:PhoneNumberId (número de WhatsApp Business)."
        : string.IsNullOrWhiteSpace(_options.AccessToken) ? "Falta WhatsApp:AccessToken (token de la Cloud API)."
        : null;

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(MissingSetup);
        }

        var text = $"{subject}\n{body}";
        object payload = string.IsNullOrWhiteSpace(_options.TemplateName)
            ? new { messaging_product = "whatsapp", to = NormalizePhone(recipient), type = "text", text = new { body = text } }
            : new
            {
                messaging_product = "whatsapp",
                to = NormalizePhone(recipient),
                type = "template",
                template = new
                {
                    name = _options.TemplateName,
                    language = new { code = _options.LanguageCode },
                    // Las plantillas no admiten saltos de línea ni tabulaciones dentro de un parámetro.
                    components = new[] { new { type = "body", parameters = new[] { new { type = "text", text = Flatten(text) } } } },
                },
            };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{_options.ApiVersion}/{_options.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"WhatsApp respondió {(int)response.StatusCode}: {Truncate(detail, 300)}");
        }
    }

    /// <summary>Deja solo dígitos; a un número local de 8 dígitos le antepone el código de país.</summary>
    public string NormalizePhone(string phone)
    {
        var digits = NonDigits().Replace(phone, string.Empty);
        return digits.Length == 8 ? _options.DefaultCountryCode + digits : digits;
    }

    private static string Flatten(string text) => Whitespace().Replace(text, " ").Trim();

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
