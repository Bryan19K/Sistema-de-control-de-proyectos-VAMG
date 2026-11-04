using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Tekcom.Gestion.Application.Notifications;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Infrastructure.Notifications;

public sealed class EmailChannel(IOptions<EmailOptions> options) : INotificationChannel
{
    private readonly EmailOptions _options = options.Value;

    public NotificationChannelKind Kind => NotificationChannelKind.Email;

    public bool IsConfigured => MissingSetup is null;

    public string? MissingSetup =>
        string.IsNullOrWhiteSpace(_options.Host) ? "Falta Email:Host (servidor SMTP)."
        : string.IsNullOrWhiteSpace(_options.FromAddress) ? "Falta Email:FromAddress (correo remitente)."
        : null;

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(MissingSetup);
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress!));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host!, _options.Port,
            _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);
        if (!string.IsNullOrWhiteSpace(_options.User))
        {
            await client.AuthenticateAsync(_options.User, _options.Password ?? string.Empty, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
