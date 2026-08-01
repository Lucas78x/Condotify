namespace CondotifyAPI.Data.Equipments;

/// <summary>
/// Resposta de abertura de sessao de video. NAO acrescente campos sem antes
/// verificar CftvStreamingContractTests: nada aqui pode revelar credencial,
/// URL RTSP ou endereco interno do equipamento.
/// </summary>
public sealed record CftvSessionOut(
    Guid SessionId,
    string PlaybackUrl,
    string Token,
    DateTime ExpiresAt,
    string Protocol);

public sealed record OpenCftvSessionIn(int Channel = 1, string Quality = "main", string Protocol = "webrtc");

public sealed record CftvStatusOut(
    Guid DeviceId,
    string Name,
    bool Online,
    DateTime? LastSeenAt,
    string HealthMessage,
    int MaxChannels);

/// <summary>Corpo enviado pelo MediaMTX ao callback de autorizacao.</summary>
public sealed class MediaAuthIn
{
    public string Action { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
