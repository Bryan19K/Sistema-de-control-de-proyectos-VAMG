using Hangfire;
using Microsoft.Extensions.Logging;
using Tekcom.Gestion.Application.Notifications;

namespace Tekcom.Gestion.Infrastructure.Notifications;

/// <summary>Trabajos programados de Hangfire. Cada uno corre con su propio alcance de servicios.</summary>
public sealed class NotificationJobs(INotificationService notifications, ILogger<NotificationJobs> logger)
{
    public const string ChecksJobId = "avisos-revision-horaria";
    public const string SummaryJobId = "avisos-resumen-diario";

    public async Task NotifyTaskAssignedAsync(int taskId, string? assignedByUserId, CancellationToken ct) =>
        await notifications.NotifyTaskAssignedAsync(taskId, assignedByUserId, ct);

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunChecksAsync(CancellationToken ct)
    {
        var sent = await notifications.RunChecksAsync(ct);
        logger.LogInformation("Revisión de avisos terminada: {Sent} enviado(s).", sent);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task SendDailySummaryAsync(CancellationToken ct)
    {
        var sent = await notifications.SendDailySummaryAsync(ct);
        logger.LogInformation("Resumen diario terminado: {Sent} enviado(s).", sent);
    }
}
