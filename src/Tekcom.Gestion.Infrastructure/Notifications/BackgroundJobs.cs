using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Notifications;

namespace Tekcom.Gestion.Infrastructure.Notifications;

/// <summary>Cola real: Hangfire. El aviso sale en segundo plano, con reintentos si el correo falla.</summary>
public sealed class HangfireNotificationDispatcher(IBackgroundJobClient client, ICurrentUserService currentUser) : INotificationDispatcher
{
    public void TaskAssigned(int taskId)
    {
        var by = currentUser.UserId;
        client.Enqueue<NotificationJobs>(j => j.NotifyTaskAssignedAsync(taskId, by, CancellationToken.None));
    }
}

/// <summary>Sin Hangfire (Hangfire:Enabled=false) no hay cola: los avisos por evento simplemente no salen.</summary>
public sealed class NullNotificationDispatcher : INotificationDispatcher
{
    public void TaskAssigned(int taskId)
    {
    }
}

public static class BackgroundJobs
{
    public static IServiceCollection AddBackgroundJobs(IServiceCollection services, IConfiguration configuration,
        string connectionString, bool useSqlite)
    {
        if (!configuration.GetValue("Hangfire:Enabled", true))
        {
            services.AddSingleton<INotificationDispatcher, NullNotificationDispatcher>();
            return services;
        }

        services.AddHangfire(cfg =>
        {
            cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();

            if (useSqlite)
            {
                // Desarrollo y pruebas sin PostgreSQL: la cola vive en memoria y se pierde al reiniciar.
                cfg.UseInMemoryStorage();
            }
            else
            {
                cfg.UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString),
                    new Hangfire.PostgreSql.PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        PrepareSchemaIfNecessary = true,
                        QueuePollInterval = TimeSpan.FromSeconds(15),
                    });
            }
        });
        services.AddHangfireServer(options => options.WorkerCount = 2);
        services.AddScoped<INotificationDispatcher, HangfireNotificationDispatcher>();
        return services;
    }

    /// <summary>Registra los trabajos recurrentes (idempotente: se actualiza si ya existen).</summary>
    public static void ScheduleNotificationJobs(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Hangfire:Enabled", true))
        {
            return;
        }

        var manager = services.GetRequiredService<IRecurringJobManager>();
        var zone = TimeZoneFor(configuration["Hangfire:TimeZone"] ?? "America/El_Salvador");
        var options = new RecurringJobOptions { TimeZone = zone };

        // Revisión de vencimientos, atrasos y stock: cada hora, de 7:00 a 18:00.
        manager.AddOrUpdate<NotificationJobs>(NotificationJobs.ChecksJobId,
            j => j.RunChecksAsync(CancellationToken.None), configuration["Hangfire:ChecksCron"] ?? "0 7-18 * * *", options);

        // Foto diaria del avance: todos los días a las 23:30.
        manager.AddOrUpdate<NotificationJobs>(NotificationJobs.SnapshotJobId,
            j => j.CaptureSnapshotsAsync(CancellationToken.None), configuration["Hangfire:SnapshotCron"] ?? "30 23 * * *", options);

        // Resumen diario: todos los días a las 7:00.
        manager.AddOrUpdate<NotificationJobs>(NotificationJobs.SummaryJobId,
            j => j.SendDailySummaryAsync(CancellationToken.None), configuration["Hangfire:SummaryCron"] ?? "0 7 * * *", options);
    }

    private static TimeZoneInfo TimeZoneFor(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
