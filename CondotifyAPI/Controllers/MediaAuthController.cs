using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Services.CFTV;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
        if (IsAuthorizedInternalPublisher(input))
            return Ok();

        // Para clientes externos, somente leitura e permitida por token.
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

    private static bool IsAuthorizedInternalPublisher(MediaAuthIn input)
    {
        if (!string.Equals(input.Action, "publish", StringComparison.OrdinalIgnoreCase) ||
            !IPAddress.TryParse(input.Ip, out var address) ||
            !IPAddress.IsLoopback(address))
            return false;

        var provided = QueryHelpers.ParseQuery(input.Query).TryGetValue("internal", out var values)
            ? values.ToString()
            : string.Empty;
        var expected = Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET") ?? string.Empty;
        if (provided.Length == 0 || expected.Length == 0) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }
}
