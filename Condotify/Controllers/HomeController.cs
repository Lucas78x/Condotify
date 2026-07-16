using Condotify.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public HomeController(
        ILogger<HomeController> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        return RedirectToAction("Index", "Licencas");
    }

    public async Task<IActionResult> Inicio()
    {
        if (!Request.Cookies.TryGetValue("AuthToken", out var token))
            return RedirectToAction("Login", "Login");

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            var validateResponse = await client.GetAsync(BuildApiUrl("api/auth/validate"));

            if (!validateResponse.IsSuccessStatusCode)
            {
                Response.Cookies.Delete("AuthToken");
                return RedirectToAction("Login", "Login");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao validar token");
            Response.Cookies.Delete("AuthToken");
            return RedirectToAction("Login", "Login");
        }

        List<LicenseViewModel> licencas = new();
        try
        {
            var response = await client.GetAsync(BuildApiUrl("api/access/licenses"));

            if (response.IsSuccessStatusCode)
            {
                licencas = await response.Content.ReadFromJsonAsync<List<LicenseViewModel>>() ?? new();
            }
            else
            {
                _logger.LogWarning("Erro ao buscar licencas: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao chamar API de licencas");
        }

        return View(licencas);
    }

    public IActionResult Login()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    private string BuildApiUrl(string path)
    {
        var baseUrl = _configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:7118";
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }
}
