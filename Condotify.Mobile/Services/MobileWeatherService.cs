using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Condotify.Mobile.Core;
using MudBlazor;

namespace Condotify.Mobile.Services;

public sealed record WeatherSnapshot(double TemperatureC, string Description, string IconKey);

/// <summary>
/// Best-effort current weather for the Home screen. Every failure mode (permission denied,
/// no GPS fix, no network, a malformed Open-Meteo response) resolves to null rather than
/// throwing - weather is a nice-to-have strip on Home, never a reason to break the page.
/// </summary>
public sealed class MobileWeatherService(IHttpClientFactory clients)
{
    public async Task<WeatherSnapshot?> GetCurrentWeatherAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var location = await ResolveLocationAsync(cancellationToken);
            if (location is null) return null;

            var client = clients.CreateClient("OpenMeteo");
            var lat = location.Latitude.ToString(CultureInfo.InvariantCulture);
            var lon = location.Longitude.ToString(CultureInfo.InvariantCulture);
            var response = await client.GetFromJsonAsync<OpenMeteoResponse>(
                $"v1/forecast?latitude={lat}&longitude={lon}&current_weather=true",
                cancellationToken);

            var current = response?.CurrentWeather;
            if (current is null) return null;

            var (description, category) = MobileWeatherCodes.Describe(current.WeatherCode);
            return new WeatherSnapshot(current.Temperature, description, IconFor(category));
        }
        catch
        {
            return null;
        }
    }

    private static string IconFor(string category) => category switch
    {
        "sunny" => Icons.Material.Outlined.WbSunny,
        "cloudy" => Icons.Material.Outlined.WbCloudy,
        "overcast" => Icons.Material.Outlined.Cloud,
        "drizzle" => Icons.Material.Outlined.Grain,
        "rain" => Icons.Material.Outlined.Umbrella,
        "snow" => Icons.Material.Outlined.AcUnit,
        "storm" => Icons.Material.Outlined.Thunderstorm,
        _ => Icons.Material.Outlined.CloudQueue
    };

    private static async Task<Location?> ResolveLocationAsync(CancellationToken cancellationToken)
    {
        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted) return null;

        var lastKnown = await Geolocation.Default.GetLastKnownLocationAsync();
        if (lastKnown is not null) return lastKnown;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
        var request = new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(6));
        return await Geolocation.Default.GetLocationAsync(request, timeoutCts.Token);
    }

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("current_weather")]
        public CurrentWeatherData? CurrentWeather { get; set; }
    }

    private sealed class CurrentWeatherData
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
        [JsonPropertyName("weathercode")]
        public int WeatherCode { get; set; }
    }
}
