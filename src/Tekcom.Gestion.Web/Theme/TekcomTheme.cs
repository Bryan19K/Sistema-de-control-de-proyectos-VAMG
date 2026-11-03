using MudBlazor;

namespace Tekcom.Gestion.Web.Theme;

/// <summary>
/// Tema visual de TEK-COM. Los valores salen de docs/diseno/GUIA_DISENO.md:
/// azul marino como color institucional y naranja como acción principal.
/// </summary>
public static class TekcomTheme
{
    public const string NavyBlue = "#12304f";
    public const string Orange = "#e8742c";
    public const string PageBackground = "#f3f5f8";
    public const string CardBorder = "#e3e8ef";
    public const string TextPrimary = "#1f2a37";
    public const string TextSecondary = "#5b6b7c";

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = NavyBlue,
            PrimaryContrastText = "#ffffff",
            Secondary = Orange,
            SecondaryContrastText = "#ffffff",
            Tertiary = "#1d5fcf",
            AppbarBackground = NavyBlue,
            AppbarText = "#ffffff",
            DrawerBackground = NavyBlue,
            DrawerText = "#d6dee8",
            DrawerIcon = "#9fb0c4",
            Background = PageBackground,
            Surface = "#ffffff",
            TextPrimary = TextPrimary,
            TextSecondary = TextSecondary,
            LinesDefault = CardBorder,
            TableLines = "#edf1f5",
            Divider = CardBorder,
            Success = "#1e8e4a",
            Warning = "#b7791f",
            Error = "#c62828",
            Info = "#1d5fcf",
            ActionDefault = TextSecondary,
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5b8fc9",
            Secondary = Orange,
            AppbarBackground = "#0c1f33",
            DrawerBackground = "#0c1f33",
            Background = "#0f1b29",
            Surface = "#172a3d",
        },
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "248px",
            AppbarHeight = "56px",
            DefaultBorderRadius = "10px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "Segoe UI", "Helvetica", "Arial", "sans-serif"],
                FontSize = "0.9375rem",
                LineHeight = "1.5",
            },
            H4 = new H4Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "700", FontSize = "1.75rem", LineHeight = "1.2" },
            H5 = new H5Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "700", FontSize = "1.375rem", LineHeight = "1.25" },
            H6 = new H6Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "1.125rem", LineHeight = "1.3" },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "1rem" },
            Button = new ButtonTypography { FontFamily = ["Inter", "sans-serif"], FontWeight = "600", FontSize = "0.9rem", TextTransform = "none" },
        },
    };
}
