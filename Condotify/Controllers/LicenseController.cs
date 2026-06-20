using Condotify.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

public class LicencasController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LicencasController> _logger;

    public LicencasController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<LicencasController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!TryCreateAuthorizedClient(out var client))
            return RedirectToAction("Login", "Login");

        var licenses = new List<LicenseViewModel>();

        try
        {
            var response = await client.GetAsync(BuildApiUrl("api/access/licenses"));
            if (response.IsSuccessStatusCode)
            {
                licenses = await response.Content.ReadFromJsonAsync<List<LicenseViewModel>>() ?? new();
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Response.Cookies.Delete("AuthToken");
                return RedirectToAction("Login", "Login");
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

        return View("~/Views/Home/Inicio.cshtml", licenses);
    }

    [HttpGet]
    public async Task<IActionResult> Detalhes(Guid id)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Result != null) return license.Result;

        return View(license.Value);
    }

    [HttpGet]
    public async Task<IActionResult> Equipamentos(Guid id)
    {
        var page = await BuildAccessDevicePage(id);
        if (page.Result != null) return page.Result;

        return View(page.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Equipamentos(Guid id, AccessDeviceFormViewModel model)
    {
        model.LicenseId = id;

        if (!ModelState.IsValid)
        {
            await PopulateModuleHeader(model, id, "equipamentos");
            model.Devices = await GetAccessDevices(id);
            return View(model);
        }

        var payload = new
        {
            LicenseId = id.ToString(),
            model.Name,
            model.IPAddress,
            model.Port,
            model.Username,
            model.Password,
            model.MACAddress,
            Model = model.DeviceModel,
            model.SerialNumber,
            model.FirmwareVersion,
            Type = model.Type,
            model.IsActive,
            Location = new { X = model.LocationX, Y = model.LocationY }
        };

        await PostToApi("api/access/devices/by-license", payload, "Equipamento cadastrado.", "Nao foi possivel cadastrar o equipamento.", includeApiKey: true);
        return RedirectToAction(nameof(Equipamentos), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Cameras(Guid id)
    {
        var page = await BuildCftvPage(id);
        if (page.Result != null) return page.Result;

        return View(page.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cameras(Guid id, CftvDeviceFormViewModel model)
    {
        model.LicenseId = id;

        if (!ModelState.IsValid)
        {
            await PopulateModuleHeader(model, id, "cameras");
            model.Devices = await GetCftvDevices(id);
            return View(model);
        }

        var payload = new
        {
            LicenseId = id.ToString(),
            model.Name,
            model.IpAddress,
            model.UserName,
            model.Password,
            model.HTTPPort,
            model.RTSPPort,
            model.IpType,
            model.Proportion,
            model.Mark,
            model.DeviceType,
            model.MaxChannels,
            Channels = Enumerable.Range(1, Math.Max(1, model.MaxChannels))
                .Select(channel => new
                {
                    ChannelNumber = channel,
                    Name = $"Canal {channel}",
                    RtspPath = string.Empty,
                    IsEnabled = true
                })
                .ToList()
        };

        await PostToApi("api/access/cftv/by-license", payload, "Camera ou gravador cadastrado.", "Nao foi possivel cadastrar a camera.", includeApiKey: true);
        return RedirectToAction(nameof(Cameras), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Blocos(Guid id)
    {
        var page = await BuildBlocksPage(id);
        if (page.Result != null) return page.Result;

        return View(page.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Blocos(Guid id, BlockFormViewModel form)
    {
        await PostToApi($"api/access/licenses/{id}/blocks", form, "Bloco cadastrado.", "Nao foi possivel cadastrar o bloco.");
        return RedirectToAction(nameof(Blocos), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Unidades(Guid id)
    {
        var page = await BuildUnitsPage(id);
        if (page.Result != null) return page.Result;

        return View(page.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unidades(Guid id, UnitFormViewModel form)
    {
        await PostToApi($"api/access/licenses/{id}/units", form, "Unidade cadastrada.", "Nao foi possivel cadastrar a unidade.");
        return RedirectToAction(nameof(Unidades), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Moradores(Guid id)
    {
        var page = await BuildResidentsPage(id);
        if (page.Result != null) return page.Result;

        return View(page.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Moradores(Guid id, ResidentFormViewModel form)
    {
        await PostToApi($"api/access/licenses/{id}/residents", form, "Morador cadastrado.", "Nao foi possivel cadastrar o morador.");
        return RedirectToAction(nameof(Moradores), new { id });
    }

    private async Task<(LicenseFullViewModel? Value, IActionResult? Result)> GetLicenseOrRedirect(Guid id)
    {
        if (!TryCreateAuthorizedClient(out var client))
            return (null, RedirectToAction("Login", "Login"));

        try
        {
            var response = await client.GetAsync(BuildApiUrl($"api/access/licenses/{id}"));
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, NotFound());

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Response.Cookies.Delete("AuthToken");
                return (null, RedirectToAction("Login", "Login"));
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Erro ao buscar licenca {LicenseId}: {StatusCode}", id, response.StatusCode);
                return (null, RedirectToAction(nameof(Index)));
            }

            var license = await response.Content.ReadFromJsonAsync<LicenseFullViewModel>();
            return license == null ? (null, NotFound()) : (license, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao buscar detalhes da licenca {LicenseId}", id);
            return (null, RedirectToAction(nameof(Index)));
        }
    }

    private async Task<(AccessDeviceFormViewModel? Value, IActionResult? Result)> BuildAccessDevicePage(Guid id)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Result != null) return (null, license.Result);

        var model = new AccessDeviceFormViewModel
        {
            Devices = await GetAccessDevices(id)
        };
        PopulateModuleHeader(model, license.Value!, "equipamentos");
        ApplyMessages(model);
        return (model, null);
    }

    private async Task<(CftvDeviceFormViewModel? Value, IActionResult? Result)> BuildCftvPage(Guid id)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Result != null) return (null, license.Result);

        var model = new CftvDeviceFormViewModel
        {
            Devices = await GetCftvDevices(id)
        };
        PopulateModuleHeader(model, license.Value!, "cameras");
        ApplyMessages(model);
        return (model, null);
    }

    private async Task<(BlocksPageViewModel? Value, IActionResult? Result)> BuildBlocksPage(Guid id)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Result != null) return (null, license.Result);

        var model = new BlocksPageViewModel
        {
            Blocks = await GetStructureBlocks(id)
        };
        PopulateModuleHeader(model, license.Value!, "blocos");
        ApplyMessages(model);
        return (model, null);
    }

    private async Task<(UnitsPageViewModel? Value, IActionResult? Result)> BuildUnitsPage(Guid id)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Result != null) return (null, license.Result);

        var model = new UnitsPageViewModel
        {
            Blocks = await GetStructureBlocks(id)
        };
        PopulateModuleHeader(model, license.Value!, "unidades");
        ApplyMessages(model);
        return (model, null);
    }

    private async Task<(ResidentsPageViewModel? Value, IActionResult? Result)> BuildResidentsPage(Guid id)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Result != null) return (null, license.Result);

        var model = new ResidentsPageViewModel
        {
            Blocks = await GetStructureBlocks(id)
        };
        PopulateModuleHeader(model, license.Value!, "moradores");
        ApplyMessages(model);
        return (model, null);
    }

    private async Task<List<BlockRowViewModel>> GetStructureBlocks(Guid licenseId)
    {
        return await GetFromApi<LicenseStructureViewModel>($"api/access/licenses/{licenseId}/structure") is { } structure
            ? structure.Blocks
            : new List<BlockRowViewModel>();
    }

    private async Task<List<AccessDeviceRowViewModel>> GetAccessDevices(Guid licenseId)
    {
        return await GetFromApi<List<AccessDeviceRowViewModel>>($"api/access/licenses/{licenseId}/devices") ?? new();
    }

    private async Task<List<CftvDeviceRowViewModel>> GetCftvDevices(Guid licenseId)
    {
        return await GetFromApi<List<CftvDeviceRowViewModel>>($"api/access/licenses/{licenseId}/cftv") ?? new();
    }

    private async Task<T?> GetFromApi<T>(string path)
    {
        if (!TryCreateAuthorizedClient(out var client))
            return default;

        var response = await client.GetAsync(BuildApiUrl(path));
        if (!response.IsSuccessStatusCode)
            return default;

        return await response.Content.ReadFromJsonAsync<T>();
    }

    private async Task PostToApi(string path, object payload, string successMessage, string errorMessage, bool includeApiKey = false)
    {
        if (!TryCreateAuthorizedClient(out var client))
        {
            TempData["ErrorMessage"] = "Sessao expirada. Faca login novamente.";
            return;
        }

        if (includeApiKey)
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

        var response = await client.PostAsJsonAsync(BuildApiUrl(path), payload);
        if (response.IsSuccessStatusCode)
            TempData["SuccessMessage"] = successMessage;
        else
            TempData["ErrorMessage"] = await ReadApiError(response, errorMessage);
    }

    private async Task PopulateModuleHeader(LicenseModuleViewModel model, Guid id, string activeTab)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Value != null)
            PopulateModuleHeader(model, license.Value, activeTab);
    }

    private static void PopulateModuleHeader(LicenseModuleViewModel model, LicenseFullViewModel license, string activeTab)
    {
        model.LicenseId = license.Id;
        model.LicenseName = license.Name;
        model.ActiveTab = activeTab;
    }

    private void ApplyMessages(LicenseModuleViewModel model)
    {
        model.SuccessMessage = TempData["SuccessMessage"] as string;
        model.ErrorMessage = TempData["ErrorMessage"] as string;
    }

    private bool TryCreateAuthorizedClient(out HttpClient client)
    {
        client = _httpClientFactory.CreateClient();

        if (!Request.Cookies.TryGetValue("AuthToken", out var token) || string.IsNullOrWhiteSpace(token))
            return false;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    private string BuildApiUrl(string path)
    {
        var baseUrl = _configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:5001";
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private string GetApiKey()
    {
        return _configuration["CondotifyApi:ApiKey"]
            ?? Environment.GetEnvironmentVariable("CT_UserAccess_API_KEY")
            ?? "1";
    }

    private static async Task<string> ReadApiError(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) ? fallback : body;
    }
}
