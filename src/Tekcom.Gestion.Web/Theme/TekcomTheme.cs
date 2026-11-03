using MudBlazor;

namespace Tekcom.Gestion.Web.Theme;

/// <summary>
/// Tema de colores de TEK-COM: azul marino como color principal y naranja
/// como acento. Centralizado aquí para que exportes y UI usen los mismos valores.
/// </summary>
public static class TekcomTheme
{
    public const string NavyBlue = "#12304f";
    public const string Orange = "#e8742c";

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = NavyBlue,
            PrimaryContrastText = "#ffffff",
            Secondary = Orange,
            SecondaryContrastText = "#ffffff",
            AppbarBackground = NavyBlue,
            AppbarText = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = "#1f2a37",
            DrawerIcon = NavyBlue,
            Background = "#f4f6f9",
            Surface = "#ffffff",
            TextPrimary = "#1f2a37",
            TextSecondary = "#5b6b7c",
            Success = "#2e7d32",
            Warning = "#ed6c02",
            Error = "#c62828",
            Info = "#0277bd",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5b8fc9",
            Secondary = Orange,
            AppbarBackground = "#0c1f33",
            DrawerBackground = "#132436",
            Background = "#0f1b29",
            Surface = "#172a3d",
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "260px",
            AppbarHeight = "60px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Roboto", "Segoe UI", "Helvetica", "Arial", "sans-serif"],
            },
        },
    };
}
