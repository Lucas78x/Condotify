using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Services.CFTV;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace CondotifyAPI.Controllers;

/// <summary>
/// Chamado pelo MediaMTX a cada leitura de midia. So deve ser alcancavel pela
/// rede interna: o MediaMTX nao autentica esta chamada.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal/media-auth")]
public sealed class MediaAuthController : ControllerBase
{
    private readonly IMediaAccessTokenService _tokens;
    private readonly ILogger<MediaAuthController> _logger;

    public MediaAuthController(IMediaAccessTokenService tokens, ILogger<MediaAuthController> logger)
    {
        _tokens = tokens;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Authorize([FromBody] MediaAuthIn input)
    {
        // Somente leitura e permitida por token. Publicacao e API nunca.
        if (!string.Equals(input.Action, "read", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        // Esta versao do MediaMTX nao envia um campo token: ele vem na query.
        var token = QueryHelpers.ParseQuery(input.Query).TryGetValue("token", out var values)
            ? values.ToString()
            : string.Empty;

        var grant = _tokens.Validate(token, input.Path);
        if (grant is null)
        {
            _logger.LogWarning(
                "Leitura de video recusada. Caminho {Path}, protocolo {Protocol}, origem {Ip}.",
                input.Path, input.Protocol, input.Ip);
            return Unauthorized();
        }

        _logger.LogInformation(
            "Leitura de video autorizada. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}, usuario {UserId}, protocolo {Protocol}, origem {Ip}.",
            grant.LicenseId, grant.DeviceId, grant.Channel, grant.UserId, input.Protocol, input.Ip);

        return Ok();
    }
}
