using MudBlazor;

namespace Condotify.UI;

/// <summary>
/// Fonte unica do tema visual da F&F Access, compartilhada entre o portal web
/// e o aplicativo mobile. Nao definir cores, raios ou tipografia fixos nos
/// componentes: acrescentar aqui.
/// </summary>
public static class CondotifyTheme
{
    public const string PrimaryColor = "#092557";
    public const string PrimaryStrongColor = "#061A3D";
    public const string PrimaryMutedColor = "#20364A";
    public const string PrimarySoftColor = "#EDF2F8";
    public const string AccentColor = "#7BC053";
    public const string AccentBrightColor = "#9DCA66";
    public const string AccentStrongColor = "#5A9E38";
    public const string AccentSoftColor = "#EFF7E9";

    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = PrimaryColor,
            Secondary = AccentColor,
            Tertiary = AccentBrightColor,
            Success = "#12805A",
            Warning = "#A96300",
            Error = "#BF3548",
            Info = "#176D91",
            Background = "#F3F5F8",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#4E5A6D",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1C2431",
            TextPrimary = "#1C2431",
            TextSecondary = "#687386",
            LinesDefault = "#DDE3EA"
        },
        PaletteDark = new PaletteDark
        {
            Primary = AccentBrightColor,
            Secondary = AccentColor,
            Tertiary = "#B6D98C",
            Success = "#3DD68C",
            Warning = "#F0A73C",
            Error = "#F2707F",
            Info = "#54B6DC",
            Background = "#14181F",
            Surface = "#1C222B",
            DrawerBackground = "#1C222B",
            DrawerText = "#B7C0CE",
            AppbarBackground = "#1C222B",
            AppbarText = "#E8ECF2",
            TextPrimary = "#E8ECF2",
            TextSecondary = "#9AA7B8",
            LinesDefault = "#2C3542"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "7px",
            DrawerWidthLeft = "256px",
            AppbarHeight = "68px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Inter", "Segoe UI", "sans-serif"] },
            H1 = new H1Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontSize = "1.7rem", FontWeight = "700", LineHeight = "1.22" },
            H2 = new H2Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontSize = "1.4rem", FontWeight = "700", LineHeight = "1.3" },
            H5 = new H5Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontSize = "1.05rem", FontWeight = "650" },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontWeight = "650" }
        }
    };
}
