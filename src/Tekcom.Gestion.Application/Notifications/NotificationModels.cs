using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Application.Notifications;

/// <summary>Un canal de salida (correo, WhatsApp). Si no está configurado, simplemente no envía.</summary>
public interface INotificationChannel
{
    NotificationChannelKind Kind { get; }
    bool IsConfigured { get; }

    /// <summary>Qué falta para activarlo (se muestra en Configuración). Vacío si ya está configurado.</summary>
    string? MissingSetup { get; }

    Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default);
}

public sealed record ChannelStatus(NotificationChannelKind Kind, bool IsConfigured, string? MissingSetup);

public interface INotificationService
{
    Task<List<NotificationRule>> RulesAsync(CancellationToken ct = default);
    Task SaveRuleAsync(NotificationType type, bool enabled, bool sendEmail, bool sendWhatsApp, CancellationToken ct = default);
    Task<List<NotificationLog>> LogAsync(int take = 200, CancellationToken ct = default);
    IReadOnlyList<ChannelStatus> Channels();

    /// <summary>Avisa a quien recibió la tarea. No hace nada si no tiene responsable, si ya se avisó o si se la asignó a sí mismo.</summary>
    Task<int> NotifyTaskAssignedAsync(int taskId, string? assignedByUserId = null, CancellationToken ct = default);

    /// <summary>Tareas por vencer y vencidas, sprints y proyectos atrasados, stock bajo. Seguro de repetir: no duplica avisos del mismo día.</summary>
    Task<int> RunChecksAsync(CancellationToken ct = default);

    Task<int> SendDailySummaryAsync(CancellationToken ct = default);

    /// <summary>Envía un mensaje de prueba al usuario por el canal elegido y lo registra en la bitácora.</summary>
    Task<NotificationLog> SendTestAsync(string userId, NotificationChannelKind channel, CancellationToken ct = default);
}

/// <summary>Pone un aviso en cola para no hacer esperar a quien guarda (la cola real es Hangfire).</summary>
public interface INotificationDispatcher
{
    void TaskAssigned(int taskId);
}
