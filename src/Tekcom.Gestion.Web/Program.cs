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

// Endpoints adicionales que necesitan las páginas de /Account.
app.MapAdditionalIdentityEndpoints();

// Base de datos: aplica migraciones pendientes y crea roles + administrador.
// Se puede desactivar con Database:ApplyMigrationsOnStartup=false (p. ej. si las migraciones se aplican desde CI).
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", defaultValue: true))
{
    await InitializeDatabaseAsync(app);
}

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    await IdentitySeeder.SeedAsync(app.Services);
}
