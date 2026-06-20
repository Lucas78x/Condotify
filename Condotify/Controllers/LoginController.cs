using Condotify.Models;
using Condotify.Out;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net.Http.Headers;

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
            if (Request.Cookies.TryGetValue("AuthToken", out var token))
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                try
                {
                    var response = await client.GetAsync(BuildApiUrl("api/auth/validate"));

                    if (response.IsSuccessStatusCode)
                    {
                        return RedirectToAction("Index", "Home");
                    }
                    else
                    {
                        Response.Cookies.Delete("AuthToken");
                    }
                }
                catch
                {
                    Response.Cookies.Delete("AuthToken");
                }
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
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
                var response = await client.PostAsJsonAsync(BuildApiUrl("api/auth/login"), new
                {
                    Email = model.Email,
                    Password = model.Password
                });

                var result = await response.Content.ReadFromJsonAsync<LoginOut>();

                if (response.IsSuccessStatusCode
                    && result != null
                    && result.Result == "Success"
                    && !string.IsNullOrWhiteSpace(result.AccessToken))
                {
                    Response.Cookies.Append("AuthToken", result.AccessToken, new Microsoft.AspNetCore.Http.CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict
                    });

                    return RedirectToAction("Index", "Home");
                }

                ModelState.AddModelError("", result?.Result ?? "Erro ao realizar login");
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao chamar API de login");
                ModelState.AddModelError("", "Erro ao processar login. Tente novamente mais tarde.");
                return View(model);
            }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        private string BuildApiUrl(string path)
        {
            var baseUrl = _configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:5001";
            return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        }
    }
}
