using Condotify.Models;
using Condotify.Out;
using Condotify.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Condotify.Controllers
{
    public class LoginController : Controller
    {
        private readonly ILogger<LoginController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly WebSessionRefreshCoordinator _refreshCoordinator;

        public LoginController(
            ILogger<LoginController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            WebSessionRefreshCoordinator refreshCoordinator)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _refreshCoordinator = refreshCoordinator;
        }


        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            var destination = ResolveLocalReturnUrl(returnUrl);
            if (User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    var token = User.FindFirstValue(ClaimsSessionContextProvider.AccessTokenClaim);
                    if (!string.IsNullOrWhiteSpace(token) && await ValidateAccessTokenAsync(token, HttpContext.RequestAborted))
                        return Redirect(destination);

                    if (await RefreshCookieSessionAsync(HttpContext.RequestAborted) is not null)
                        return Redirect(destination);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nao foi possivel validar ou renovar a sessao web");
                }

                await SignOutLocalAsync();
            }

            return View(new LoginViewModel { ReturnUrl = destination });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (model.MfaRequired)
            {
                ModelState.Remove(nameof(model.Email));
                ModelState.Remove(nameof(model.Password));
                if (string.IsNullOrWhiteSpace(model.MfaChallengeToken) || string.IsNullOrWhiteSpace(model.MfaCode))
                    ModelState.AddModelError(nameof(model.MfaCode), "Informe o código do autenticador ou um código de recuperação.");
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage);

                TempData["LoginError"] = string.Join(" • ", errors);

                return View(model);
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = model.MfaRequired
                    ? await client.PostAsJsonAsync(BuildApiUrl("api/auth/mfa/verify"), new { ChallengeToken = model.MfaChallengeToken, Code = model.MfaCode })
                    : await client.PostAsJsonAsync(BuildApiUrl("api/auth/login"), new { Email = model.Email, Password = model.Password });

                var result = await response.Content.ReadFromJsonAsync<LoginOut>();

                if (response.IsSuccessStatusCode
                    && result != null
                    && result.Result == "Success"
                    && !string.IsNullOrWhiteSpace(result.AccessToken)
                    && !string.IsNullOrWhiteSpace(result.RefreshToken))
                {
                    var principal = CreatePrincipal(result.AccessToken, result.RefreshToken, model.Email);
                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = model.RememberMe,
                        AllowRefresh = true,
                        ExpiresUtc = model.RememberMe
                            ? DateTimeOffset.UtcNow.AddDays(30)
                            : DateTimeOffset.UtcNow.AddHours(8)
                    });

                    Response.Cookies.Delete("AuthToken");
                    return principal.FindFirstValue("first_access") == "true"
                        ? Redirect("/seguranca?primeiroAcesso=1")
                        : Redirect(ResolveLocalReturnUrl(model.ReturnUrl));
                }

                if (response.IsSuccessStatusCode && result?.MfaRequired == true && !string.IsNullOrWhiteSpace(result.ChallengeToken))
                {
                    ModelState.Clear();
                    return View(new LoginViewModel
                    {
                        MfaRequired = true,
                        MfaChallengeToken = result.ChallengeToken,
                        Email = model.Email,
                        RememberMe = model.RememberMe,
                        ReturnUrl = ResolveLocalReturnUrl(model.ReturnUrl)
                    });
                }

                ModelState.AddModelError("", FriendlyLoginError(result?.Result));
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao chamar API de login");
                ModelState.AddModelError("", "Erro ao processar login. Tente novamente mais tarde.");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await RevokeRemoteSessionAsync(HttpContext.RequestAborted);
            await SignOutLocalAsync();
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> KeepAlive(CancellationToken cancellationToken)
        {
            var remaining = GetRemainingAccessTokenLifetime(User, DateTimeOffset.UtcNow);
            if (remaining is { } current && current > TimeSpan.FromMinutes(5))
                return Ok(new { Refreshed = false, ExpiresInSeconds = Math.Max(1, (long)current.TotalSeconds) });

            var refreshed = await RefreshCookieSessionAsync(cancellationToken);
            if (refreshed is null)
            {
                await SignOutLocalAsync();
                return Unauthorized(new { Result = "SessionExpired" });
            }

            var refreshedRemaining = GetRemainingAccessTokenLifetime(refreshed, DateTimeOffset.UtcNow)
                ?? TimeSpan.FromMinutes(55);
            return Ok(new
            {
                Refreshed = true,
                ExpiresInSeconds = Math.Max(1, (long)refreshedRemaining.TotalSeconds)
            });
        }

        private string BuildApiUrl(string path)
        {
            var baseUrl = _configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:7118";
            return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        private async Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.GetAsync(BuildApiUrl("api/auth/validate"), cancellationToken);
            return response.IsSuccessStatusCode;
        }

        private async Task<ClaimsPrincipal?> RefreshCookieSessionAsync(CancellationToken cancellationToken)
        {
            var refreshToken = User.FindFirstValue(ClaimsSessionContextProvider.RefreshTokenClaim);
            if (string.IsNullOrWhiteSpace(refreshToken))
                return null;

            var result = await _refreshCoordinator.RefreshAsync(
                refreshToken,
                ct => RequestRefreshAsync(refreshToken, ct),
                cancellationToken);
            if (!IsSuccessfulRefresh(result))
                return null;

            var fallbackEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var principal = CreatePrincipal(result!.AccessToken!, result.RefreshToken!, fallbackEmail);
            var authentication = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var properties = authentication.Properties ?? new AuthenticationProperties
            {
                AllowRefresh = true
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                properties);

            return principal;
        }

        private async Task<LoginOut?> RequestRefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                using var response = await client.PostAsJsonAsync(
                    BuildApiUrl("api/auth/refresh"),
                    new { RefreshToken = refreshToken },
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<LoginOut>(cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
            {
                _logger.LogWarning(ex, "A API nao renovou a sessao web");
                return null;
            }
        }

        private async Task RevokeRemoteSessionAsync(CancellationToken cancellationToken)
        {
            var accessToken = User.FindFirstValue(ClaimsSessionContextProvider.AccessTokenClaim);
            var refreshToken = User.FindFirstValue(ClaimsSessionContextProvider.RefreshTokenClaim);
            if (string.IsNullOrWhiteSpace(refreshToken))
                return;

            try
            {
                if (GetRemainingAccessTokenLifetime(User, DateTimeOffset.UtcNow) is not { } remaining
                    || remaining <= TimeSpan.Zero)
                {
                    var refreshed = await _refreshCoordinator.RefreshAsync(
                        refreshToken,
                        ct => RequestRefreshAsync(refreshToken, ct),
                        cancellationToken);
                    if (IsSuccessfulRefresh(refreshed))
                    {
                        accessToken = refreshed!.AccessToken;
                        refreshToken = refreshed.RefreshToken;
                    }
                }

                if (string.IsNullOrWhiteSpace(accessToken))
                    return;

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var response = await client.PostAsJsonAsync(
                    BuildApiUrl("api/auth/logout"),
                    new { RefreshToken = refreshToken },
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("A API recusou a revogacao da sessao web com HTTP {StatusCode}", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nao foi possivel revogar a sessao remota durante o logout");
            }
        }

        private async Task SignOutLocalAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete("AuthToken");
        }

        private static bool IsSuccessfulRefresh(LoginOut? result) =>
            result?.Result == "Success"
            && !string.IsNullOrWhiteSpace(result.AccessToken)
            && !string.IsNullOrWhiteSpace(result.RefreshToken);

        private static ClaimsPrincipal CreatePrincipal(string accessToken, string refreshToken, string fallbackEmail)
        {
            var claims = new List<Claim>
            {
                new(ClaimsSessionContextProvider.AccessTokenClaim, accessToken),
                new(ClaimsSessionContextProvider.RefreshTokenClaim, refreshToken),
                new(ClaimTypes.Email, fallbackEmail)
            };

            try
            {
                var parts = accessToken.Split('.');
                if (parts.Length >= 2)
                {
                    var payload = parts[1].Replace('-', '+').Replace('_', '/');
                    payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                    using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

                    AddClaim(json.RootElement, claims, "sub", ClaimTypes.NameIdentifier);
                    AddClaim(json.RootElement, claims, "name", ClaimTypes.Name);
                    AddClaim(json.RootElement, claims, "enterprise_id", ClaimsSessionContextProvider.EnterpriseIdClaim);
                    AddClaim(json.RootElement, claims, "access_type", "access_type");
                    AddClaim(json.RootElement, claims, "first_access", "first_access");
                    AddClaim(json.RootElement, claims, "principal_type", "principal_type");
                    AddExpirationClaim(json.RootElement, claims);
                }
            }
            catch
            {
                // A API validou o token; os dados opcionais do perfil podem ser recuperados depois.
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private static TimeSpan? GetRemainingAccessTokenLifetime(ClaimsPrincipal principal, DateTimeOffset now)
        {
            var value = principal.FindFirstValue(ClaimsSessionContextProvider.AccessTokenExpiresAtClaim);
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAt)
                ? DateTimeOffset.FromUnixTimeSeconds(expiresAt) - now
                : null;
        }

        private static TimeSpan? GetRemainingAccessTokenLifetime(LoginOut result, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(result.AccessToken))
                return null;

            try
            {
                var parts = result.AccessToken.Split('.');
                if (parts.Length < 2)
                    return null;

                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
                return json.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var expiresAt)
                    ? DateTimeOffset.FromUnixTimeSeconds(expiresAt) - now
                    : null;
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        private static string FriendlyLoginError(string? result) => result switch
        {
            "InvalidCredentials" => "E-mail ou senha incorretos.",
            "InvalidMfaCode" => "O código informado é inválido ou expirou.",
            _ => "Não foi possível entrar. Tente novamente."
        };

        public static string ResolveLocalReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl[0] != '/')
                return "/";

            return returnUrl.Length == 1 || returnUrl[1] is not ('/' or '\\')
                ? returnUrl
                : "/";
        }

        private static void AddClaim(JsonElement payload, ICollection<Claim> claims, string jsonName, string claimType)
        {
            if (!payload.TryGetProperty(jsonName, out var value)) return;

            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            if (!string.IsNullOrWhiteSpace(text) && !claims.Any(x => x.Type == claimType))
                claims.Add(new Claim(claimType, text));
        }

        private static void AddExpirationClaim(JsonElement payload, ICollection<Claim> claims)
        {
            if (payload.TryGetProperty("exp", out var value) && value.TryGetInt64(out var expiresAt))
            {
                claims.Add(new Claim(
                    ClaimsSessionContextProvider.AccessTokenExpiresAtClaim,
                    expiresAt.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }
}
