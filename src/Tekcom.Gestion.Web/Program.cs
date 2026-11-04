using Microsoft.AspNetCore.DataProtection;
using Hangfire;
using System.Globalization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Tekcom.Gestion.Application;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Infrastructure;
using Tekcom.Gestion.Infrastructure.Identity;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Web.Components;
using Tekcom.Gestion.Web.Components.Account;
using Tekcom.Gestion.Web.Services;

// Cultura por defecto: español (El Salvador) para fechas, números y moneda.
var culture = new CultureInfo("es-SV");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

// Interfaz: Blazor con render interactivo en servidor + MudBlazor.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// Autenticación con cookies de Identity.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ProjectSelection>();
builder.Services.AddScoped<UserAccess>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Capas de aplicación e infraestructura (EF Core, Identity, auditoría).
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// En el servidor las claves de protección de datos (cookies de sesión, antiforgery) deben sobrevivir
// a los reinicios del contenedor: se guardan en una carpeta persistente (DataProtection:Path).
if (builder.Configuration["DataProtection:Path"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Tekcom.Gestion")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

var app = builder.Build();

// Pipeline HTTP.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture(culture.Name)
    .AddSupportedCultures(culture.Name)
    .AddSupportedUICultures(culture.Name));

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Chequeo de salud para el despliegue (sin sesión): responde 200 si la aplicación y la base están arriba.
app.MapGet("/healthz", async (Microsoft.EntityFrameworkCore.IDbContextFactory<Tekcom.Gestion.Infrastructure.Persistence.ApplicationDbContext> factory) =>
{
    await using var db = factory.CreateDbContext();
    return await db.Database.CanConnectAsync() ? Results.Ok("ok") : Results.StatusCode(503);
}).AllowAnonymous();

// Descarga de adjuntos de tareas (solo usuarios con sesión iniciada).
app.MapGet("/adjuntos/tareas/{id:int}", async (int id, Tekcom.Gestion.Application.Services.ITaskService tasks) =>
{
    var found = await tasks.OpenAttachmentAsync(id);
    return found is null
        ? Results.NotFound()
        : Results.File(found.Value.Content, found.Value.Attachment.ContentType, found.Value.Attachment.FileName);
}).RequireAuthorization();

// Descarga de documentos de proyecto (ATP, CAES, minutas firmadas, fotos). Solo con sesión iniciada.
app.MapGet("/archivos/documentos/{id:int}", async (int id, Tekcom.Gestion.Application.Services.IDocumentService documents) =>
{
    var found = await documents.OpenAsync(id);
    return found is null
        ? Results.NotFound()
        : Results.File(found.Value.Content, found.Value.Document.ContentType, found.Value.Document.FileName);
}).RequireAuthorization();

// Plantilla de Excel para importar la proyección de diseño.
app.MapGet("/plantillas/proyeccion.xlsx", (Tekcom.Gestion.Application.Import.IProjectionImportService importer) =>
    Results.File(importer.BuildTemplate(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "plantilla-proyeccion.xlsx"))
    .RequireAuthorization();

// Descarga de reportes en Excel o PDF (solo con sesión iniciada).
app.MapGet("/exportes/{key}/{format}", async (
    string key, string format, int proyecto, int? nodo, string? corte, int? plantilla,
    System.Security.Claims.ClaimsPrincipal user,
    Tekcom.Gestion.Application.Reports.IReportService reports,
    Tekcom.Gestion.Application.Reports.IReportExporter exporter,
    Tekcom.Gestion.Application.Reports.IExportTemplateService templates,
    Tekcom.Gestion.Application.Services.IUserAdminService users) =>
{
    try
    {
        var preparedBy = await users.DisplayNameAsync(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty);
        DateOnly? cutoff = DateOnly.TryParse(corte, out var parsed) ? parsed : null;
        var document = await reports.BuildAsync(new Tekcom.Gestion.Application.Reports.ReportRequest
        {
            Key = key, ProjectId = proyecto, SiteId = nodo, CutoffDate = cutoff, PreparedBy = preparedBy,
        });
        var template = await templates.GetAsync(plantilla);

        return format.ToLowerInvariant() switch
        {
            "xlsx" => Results.File(exporter.ToExcel(document, template),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{document.FileBaseName}.xlsx"),
            "pdf" => Results.File(exporter.ToPdf(document, template), "application/pdf", $"{document.FileBaseName}.pdf"),
            _ => Results.NotFound(),
        };
    }
    catch (Tekcom.Gestion.Application.Common.BusinessRuleException ex)
    {
        return Results.BadRequest(ex.Message);
    }
}).RequireAuthorization();

// Endpoints adicionales que necesitan las páginas de /Account.
app.MapAdditionalIdentityEndpoints();

// Base de datos: aplica migraciones pendientes, siembra datos base, roles y administrador.
// Se puede desactivar con Database:ApplyMigrationsOnStartup=false (p. ej. si las migraciones se aplican desde CI).
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", defaultValue: true))
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}

// Trabajos programados (avisos) y su panel de control, solo para administradores.
Tekcom.Gestion.Infrastructure.Notifications.BackgroundJobs.ScheduleNotificationJobs(app.Services);
if (app.Configuration.GetValue("Hangfire:Enabled", true))
{
    app.MapHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
    {
        Authorization = [new Tekcom.Gestion.Web.Services.AdminDashboardAuthorizationFilter()],
        DashboardTitle = "TEK-COM · Trabajos programados",
    });
}

app.Run();
