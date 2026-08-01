using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Services.CFTV;

public enum StreamQuality
{
    Main = 0,
    Secondary = 1
}

public interface ICftvStreamPathResolver
{
    /// <summary>
    /// Caminhos que o teste de conectividade percorre. Reproduz exatamente o
    /// comportamento historico de CFTVService: nao alterar sem testar contra
    /// o equipamento correspondente.
    /// </summary>
    IReadOnlyList<string> ConnectivityProbePaths(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel);

    /// <summary>
    /// Melhor caminho conhecido para o gateway de midia. Comportamento novo:
    /// nenhum fluxo existente usa isto. Devolve null para marca desconhecida.
    /// </summary>
    string? PreferredPath(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel, StreamQuality quality);

    string BuildRtspUrl(string ip, int port, string user, string password, string path);
}

public sealed class CftvStreamPathResolver : ICftvStreamPathResolver
{
    // === Sondagem: copia literal do comportamento vivo (GetCameraTemplates / RtspPathTemplatesByBrand) ===

    private static readonly string[] AxisCameraProbe =
        ["/axis-media/media.amp", "/axis-media/media.amp?videocodec=h264"];

    private static readonly string[] GenericCameraProbe =
        ["/live", "/stream1", "/h264"];

    private static readonly Dictionary<MarkEnum, string[]> RecorderProbeByBrand = new()
    {
        [MarkEnum.Intelbras] = ["/cam/realmonitor?channel={ch}&subtype=0", "/cam/realmonitor?channel={ch}&subtype=1"],
        [MarkEnum.Dahua] = ["/cam/realmonitor?channel={ch}&subtype=0", "/cam/realmonitor?channel={ch}&subtype=1"],
        [MarkEnum.Hikvision] = ["/Streaming/Channels/{ch}01", "/Streaming/Channels/{ch}02", "/h264/ch{ch}/main/av_stream", "/h264/ch{ch}/sub/av_stream"],
        [MarkEnum.Hilook] = ["/Streaming/Channels/{ch}01", "/Streaming/Channels/{ch}02"],
        [MarkEnum.Uniview] = ["/live/ch{ch}_0", "/live/ch{ch}_1"],
        [MarkEnum.Axis] = ["/axis-media/media.amp"]
    };

    private static readonly string[] GenericRecorderProbe =
        ["/cam/realmonitor?channel={ch}&subtype=0", "/Streaming/Channels/{ch}01"];

    // === Preferencia: copia literal do dicionario morto RtspPathsByBrand, so para o gateway ===

    private static readonly Dictionary<MarkEnum, string[]> PreferredCameraByBrand = new()
    {
        [MarkEnum.Intelbras] = ["/cam/realmonitor?channel=1&subtype=0", "/cam/realmonitor?channel=1&subtype=1", "/live", "/h264"],
        [MarkEnum.Dahua] = ["/cam/realmonitor?channel=1&subtype=0", "/cam/realmonitor?channel=1&subtype=1"],
        [MarkEnum.Hikvision] = ["/Streaming/Channels/101", "/Streaming/Channels/102", "/h264/ch1/main/av_stream", "/h264/ch1/sub/av_stream"],
        [MarkEnum.Hilook] = ["/Streaming/Channels/101", "/Streaming/Channels/102", "/h264/ch1/main/av_stream", "/h264/ch1/sub/av_stream"],
        [MarkEnum.Uniview] = ["/live/0/main", "/live/0/sub", "/live/ch00_0", "/live/ch00_1"],
        [MarkEnum.Axis] = ["/axis-media/media.amp", "/axis-media/media.amp?videocodec=h264"]
    };

    public IReadOnlyList<string> ConnectivityProbePaths(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel)
    {
        if (deviceType == CFTVDeviceTypeEnum.Camera)
            return mark == MarkEnum.Axis ? AxisCameraProbe : GenericCameraProbe;

        var templates = RecorderProbeByBrand.TryGetValue(mark, out var found) ? found : GenericRecorderProbe;
        return Substitute(templates, channel);
    }

    public string? PreferredPath(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel, StreamQuality quality)
    {
        string[]? candidates;

        if (deviceType == CFTVDeviceTypeEnum.Camera)
        {
            if (!PreferredCameraByBrand.TryGetValue(mark, out candidates)) return null;
        }
        else
        {
            if (!RecorderProbeByBrand.TryGetValue(mark, out var templates)) return null;
            candidates = Substitute(templates, channel);
        }

        if (candidates.Length == 0) return null;

        var index = quality == StreamQuality.Secondary && candidates.Length > 1 ? 1 : 0;
        return candidates[index];
    }

    public string BuildRtspUrl(string ip, int port, string user, string password, string path)
    {
        if (!path.StartsWith('/')) path = "/" + path;
        var escapedUser = Uri.EscapeDataString(user ?? string.Empty);
        var escapedPassword = Uri.EscapeDataString(password ?? string.Empty);
        return $"rtsp://{escapedUser}:{escapedPassword}@{ip}:{port}{path}";
    }

    private static string[] Substitute(string[] templates, int channel) =>
        templates.Select(x => x.Replace("{ch}", channel.ToString("D2"))).ToArray();
}
