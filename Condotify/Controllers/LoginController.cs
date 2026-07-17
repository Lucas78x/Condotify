using Condotify.Models;
using Condotify.Out;
using Condotify.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
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

        public LoginController(
            ILogger<LoginController> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }


        [HttpGet]
        public async Task<IActionResult> Login()
        {
            var token = User.FindFirstValue(CondotifyApiClient.AccessTokenClaim);
            if (User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(token))
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                try
                {
                    var response = await client.GetAsync(BuildApiUrl("api/auth/validate"));

                    if (response.IsSuccessStatusCode)
                    {
                        return Redirect("/");
                    }
                    else
                    {
                        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                }
                catch
                {
                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }

            return View();
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
                    && !string.IsNullOrWhiteSpace(result.AccessToken))
                {
                    var principal = CreatePrincipal(result.AccessToken, model.Email);
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
                    return Redirect("/");
                }

                if (response.IsSuccessStatusCode && result?.MfaRequired == true && !string.IsNullOrWhiteSpace(result.ChallengeToken))
                {
                    ModelState.Clear();
                    return View(new LoginViewModel
                    {
                        MfaRequired = true,
                        MfaChallengeToken = result.ChallengeToken,
                        Email = model.Email,
                        RememberMe = model.RememberMe
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
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete("AuthToken");
            return RedirectToAction(nameof(Login));
        }

        private string BuildApiUrl(string path)
        {
            var baseUrl = _configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:7118";
            return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        private static ClaimsPrincipal CreatePrincipal(string accessToken, string fallbackEmail)
        {
            var claims = new List<Claim>
            {
                new(CondotifyApiClient.AccessTokenClaim, accessToken),
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
                    AddClaim(json.RootElement, claims, "enterprise_id", "enterprise_id");
                    AddClaim(json.RootElement, claims, "access_type", "access_type");
                }
            }
            catch
            {
                // A API validou o token; os dados opcionais do perfil podem ser recuperados depois.
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private static string FriendlyLoginError(string? result) => result switch
        {
            "InvalidCredentials" => "E-mail ou senha incorretos.",
            "InvalidMfaCode" => "O código informado é inválido ou expirou.",
            _ => "Não foi possível entrar. Tente novamente."
        };

        private static void AddClaim(JsonElement payload, ICollection<Claim> claims, string jsonName, string claimType)
        {
            if (!payload.TryGetProperty(jsonName, out var value)) return;

            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text) && !claims.Any(x => x.Type == claimType))
                claims.Add(new Claim(claimType, text));
        }
    }
}
