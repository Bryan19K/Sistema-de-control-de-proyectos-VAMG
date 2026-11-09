using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Reports;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Infrastructure.Identity;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Interceptors;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra EF Core (PostgreSQL), el interceptor de auditoría e Identity.
    /// La aplicación web debe registrar además su propio <see cref="ICurrentUserService"/>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión 'DefaultConnection'. Configúrala con user-secrets (ver README).");

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<AuditableEntityInterceptor>();

        var useSqlite = string.Equals(configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);

        void ConfigureDb(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            if (useSqlite)
            {
                // Solo para desarrollo y pruebas locales sin SQL Server.
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }

            options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
        }

        // Blazor Server: cada operación usa su propio contexto (fábrica); Identity usa el contexto del scope.
        services.AddDbContextFactory<ApplicationDbContext>(ConfigureDb, ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IDeletionService, DeletionService>();
        services.AddScoped<IQuantityService, QuantityService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IMaterialService, MaterialService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IFeederService, FeederService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IProjectionService, ProjectionService>();
        services.AddScoped<ILiquidationService, LiquidationService>();
        services.AddScoped<IWorkPlanService, WorkPlanService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IPresentationService, PresentationService>();
        services.AddScoped<ISnapshotService, SnapshotService>();
        services.AddScoped<IAttentionService, AttentionService>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<ISprintService, SprintService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IEquipmentService, EquipmentService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IMinuteService, MinuteService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IExcelImportService, Import.ExcelImportService>();
        services.AddScoped<IReportService, Reports.ReportService>();
        services.AddSingleton<IReportExporter, Reports.ReportExporter>();
        services.AddScoped<IExportTemplateService, Reports.ExportTemplateService>();
        services.AddScoped<IProjectionImportService, Import.ProjectionImportService>();
        services.AddScoped<IDataPackImporter, Import.WarehouseImportService>();
        services.AddScoped<IDataPackImporter, Import.NextWaveControlImportService>();
        services.AddScoped<IDataPackImporter, Import.NextWaveMaterialsImportService>();
        services.AddScoped<IDataPackImporter, Import.ExpensesImportService>();
        services.AddScoped<IDataPackImporter, Import.PriceListImportService>();
        services.AddScoped<IDataPackImporter, Import.ContractorsImportService>();
        services.AddScoped<IDataImportCoordinator, Import.DataImportCoordinator>();
        services.AddScoped<IFinanceService, FinanceService>();
        services.AddScoped<IBuildingService, BuildingService>();
        services.AddScoped<IContractorDirectoryService, ContractorDirectoryService>();
        services.AddScoped<ILoadVerificationService, LoadVerificationService>();

        // Avisos: correo y WhatsApp (cada canal se activa solo cuando está configurado) + Hangfire.
        services.Configure<Notifications.EmailOptions>(configuration.GetSection(Notifications.EmailOptions.SectionName));
        services.Configure<Notifications.WhatsAppOptions>(configuration.GetSection(Notifications.WhatsAppOptions.SectionName));
        services.AddSingleton<Application.Notifications.INotificationChannel, Notifications.EmailChannel>();
        services.AddHttpClient<Notifications.WhatsAppChannel>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddTransient<Application.Notifications.INotificationChannel>(sp => sp.GetRequiredService<Notifications.WhatsAppChannel>());
        services.AddScoped<Application.Notifications.INotificationService, Notifications.NotificationService>();
        services.AddScoped<Notifications.NotificationJobs>();
        Notifications.BackgroundJobs.AddBackgroundJobs(services, configuration, connectionString, useSqlite);

        services.Configure<SeedAdminOptions>(configuration.GetSection(SeedAdminOptions.SectionName));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Todavía no se exige confirmar la cuenta por correo: los usuarios los crea el administrador.
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Stores.SchemaVersion = IdentitySchema.Current;
            })
            .AddRoles<IdentityRole>()
            .AddErrorDescriber<SpanishIdentityErrorDescriber>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return services;
    }
}
