namespace Condotify.Mobile.Core;

/// <summary>Maps Open-Meteo's WMO weather codes to a short pt-BR description and an icon
/// category. Kept UI-framework-agnostic (no MudBlazor dependency) so it's testable without
/// pulling MAUI/MudBlazor into the test project - callers map the category to an actual icon.</summary>
public static class MobileWeatherCodes
{
    public static (string Description, string Category) Describe(int weatherCode) => weatherCode switch
    {
        0 => ("Céu limpo", "sunny"),
        1 or 2 => ("Parcialmente nublado", "cloudy"),
        3 => ("Nublado", "overcast"),
        45 or 48 => ("Névoa", "overcast"),
        51 or 53 or 55 or 56 or 57 => ("Garoa", "drizzle"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("Chuva", "rain"),
        71 or 73 or 75 or 77 or 85 or 86 => ("Neve", "snow"),
        95 or 96 or 99 => ("Trovoada", "storm"),
        _ => ("Tempo estável", "unknown")
    };
}
