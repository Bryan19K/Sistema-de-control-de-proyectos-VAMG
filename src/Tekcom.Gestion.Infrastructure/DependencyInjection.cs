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
    /// Registra EF Core (SQL Server), el interceptor de auditoría e Identity.
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
                options.UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }

            options.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
        }

        // Blazor Server: cada operación usa su propio contexto (fábrica); Identity usa el contexto del scope.
        services.AddDbContextFactory<ApplicationDbContext>(ConfigureDb, ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IQuantityService, QuantityService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IMaterialService, MaterialService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IFeederService, FeederService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IProjectionService, ProjectionService>();
        services.AddScoped<ILiquidationService, LiquidationService>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<ISprintService, SprintService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IExcelImportService, Import.ExcelImportService>();
        services.AddScoped<IReportService, Reports.ReportService>();
        services.AddSingleton<IReportExporter, Reports.ReportExporter>();
        services.AddScoped<IExportTemplateService, Reports.ExportTemplateService>();
        services.AddScoped<IProjectionImportService, Import.ProjectionImportService>();

        services.Configure<SeedAdminOptions>(configuration.GetSection(SeedAdminOptions.SectionName));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // No hay servicio de correo todavía (Sprint 6); no se exige confirmar la cuenta.
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
