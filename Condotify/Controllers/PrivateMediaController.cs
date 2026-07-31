using Condotify.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Security.Claims;

namespace Condotify.Controllers;

[Authorize]
[Route("private-media")]
public sealed class PrivateMediaController(IHttpClientFactory clients, IConfiguration configuration) : Controller
{
    [HttpGet("{licenseId:guid}/{mediaId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid licenseId, Guid mediaId, CancellationToken cancellationToken)
    {
        var token = User.FindFirstValue(ClaimsSessionContextProvider.AccessTokenClaim);
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        using var client = clients.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var baseUrl = configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:7118";
        using var response = await client.GetAsync($"{baseUrl.TrimEnd('/')}/api/access/licenses/{licenseId:D}/media/{mediaId:D}", cancellationToken);
        if (!response.IsSuccessStatusCode) return response.StatusCode == System.Net.HttpStatusCode.NotFound ? NotFound() : StatusCode((int)response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return File(bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
    }
}
