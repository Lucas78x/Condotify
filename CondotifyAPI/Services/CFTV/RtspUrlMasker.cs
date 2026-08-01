namespace CondotifyAPI.Services.CFTV;

/// <summary>
/// Remove usuario e senha de URLs RTSP antes que elas apareçam em resposta
/// HTTP, log ou mensagem de erro. URLs montadas por BuildRtspUrl contêm a
/// credencial do equipamento em texto claro.
/// </summary>
public static class RtspUrlMasker
{
    private const string Placeholder = "rtsp://***";

    public static string Mask(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Placeholder;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return Placeholder;

        if (string.IsNullOrEmpty(parsed.UserInfo)) return url;

        var builder = new UriBuilder(parsed)
        {
            UserName = "***",
            Password = "***"
        };

        return builder.Uri.ToString();
    }
}
