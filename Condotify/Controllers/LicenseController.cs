using Condotify.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

public class LicencasController : Controller
{
    private static readonly string[] AccessDeviceTypes =
    {
        "SS5520",
        "SS5530MFFace",
        "SS5530MFFaceLite",
        "SS3530MFFaceW",
        "SS3430Bio",
        "SS3430MFBio",
        "SS7520FaceT",
        "SS7530Face",
        "SS3530MFFace",
        "SS3540MFFaceEx",
        "SS1540MFW",
        "SS1530MFW",
        "SS3540MFFaceBioEx",
        "SS3540MFFaceBio",
        "SS3532MFW",
        "SS3532MF",
        "SS3542MFW",
        "SS3531MF",
        "SS5531MFW",
        "SS5541MFW",
        "SS5532MFW",
        "SS5542MFW",
        "SS5430MFBioFT",
        "SS3541MF",
        "CT30002PB",
        "CT30004PB",
        "SS3710UHF",
        "IdFace",
        "IdFaceMax",
        "ControlIdUHF"
    };

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
    public IActionResult Nova()
    {
        if (!TryCreateAuthorizedClient(out _))
            return RedirectToAction("Login", "Login");

        return View(new CreateLicenseViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Nova(CreateLicenseViewModel model)
    {
        if (!TryCreateAuthorizedClient(out var client))
            return RedirectToAction("Login", "Login");

        if (!TryGetEnterpriseIdFromToken(out var enterpriseId))
        {
            model.ErrorMessage = "Nao foi possivel identificar a empresa do usuario logado. Faca login novamente.";
            return View(model);
        }

        model.CNPJ = OnlyDigits(model.CNPJ);
        model.Code = string.IsNullOrWhiteSpace(model.Code)
            ? $"LIC-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : model.Code.Trim();

        if (!ModelState.IsValid)
            return View(model);

        var payload = new
        {
            EnterpriseId = enterpriseId.ToString(),
            model.Name,
            model.CNPJ,
            model.City,
            model.Country,
            model.Code,
            Organization = model.Organization,
            Building = model.Building,
            Type = model.Type,
            Location = new
            {
                Name = model.City,
                X = model.LocationX,
                Y = model.LocationY
            },
            model.ExpireDate
        };

        var response = await client.PostAsJsonAsync(BuildApiUrl("api/access/licenses/by-enterprise"), payload);
        if (!response.IsSuccessStatusCode)
        {
            model.ErrorMessage = await ReadApiError(response, "Nao foi possivel criar a licenca.");
            return View(model);
        }

        TempData["SuccessMessage"] = "Licenca criada.";
        var createdId = await TryReadCreatedLicenseId(response);
        return createdId.HasValue
            ? RedirectToAction(nameof(Detalhes), new { id = createdId.Value })
            : RedirectToAction(nameof(Index));
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
        model.DeviceModel = ResolveAccessDeviceModel(model.Type);
        if (string.IsNullOrWhiteSpace(model.DeviceModel))
            ModelState.AddModelError(nameof(model.Type), "Selecione um modelo de equipamento válido.");

        if (!ModelState.IsValid)
        {
            await PopulateModuleHeader(model, id, "equipamentos");
            model.ErrorMessage = FormatModelStateErrors(ModelState);
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
            MACAddress = OptionalString(model.MACAddress),
            Model = model.DeviceModel,
            SerialNumber = OptionalString(model.SerialNumber),
            FirmwareVersion = OptionalString(model.FirmwareVersion),
            Type = model.Type,
            model.IsActive,
            Location = new { Name = model.Name, X = model.LocationX, Y = model.LocationY }
        };

        var result = await PostToApiResult("api/access/devices/by-license", payload, "Equipamento cadastrado.", "Nao foi possivel cadastrar o equipamento.", includeApiKey: true);
        if (result.Success)
            return RedirectToAction(nameof(Equipamentos), new { id });

        await PopulateModuleHeader(model, id, "equipamentos");
        model.ErrorMessage = result.Message;
        model.Devices = await GetAccessDevices(id);
        return View(model);
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
            model.ErrorMessage = FormatModelStateErrors(ModelState);
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

    [HttpGet]
    public async Task<IActionResult> Encomendas(Guid id)
    {
        var page = await BuildDeliveriesPage(id);
        if (page.Result != null) return page.Result;

        return View(page.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Encomendas(Guid id, DeliveryFormViewModel form)
    {
        await PostToApi($"api/access/licenses/{id}/deliveries", form, "Encomenda registrada.", "Nao foi possivel registrar a encomenda.");
        return RedirectToAction(nameof(Encomendas), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AlterarStatusEncomenda(Guid id, DeliveryStatusFormViewModel form)
    {
        var payload = new
        {
            Status = form.Status,
            PersonName = form.PersonName,
            ProofUrl = form.ProofUrl
        };

        await PatchToApi($"api/access/licenses/{id}/deliveries/{form.DeliveryId}/status", payload, "Status da encomenda atualizado.", "Nao foi possivel atualizar a encomenda.");
        return RedirectToAction(nameof(Encomendas), new { id });
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

    private async Task<(DeliveriesPageViewModel? Value, IActionResult? Result)> BuildDeliveriesPage(Guid id)
    {
        var license = await GetLicenseOrRedirect(id);
        if (license.Result != null) return (null, license.Result);

        var model = new DeliveriesPageViewModel
        {
            Deliveries = await GetDeliveries(id)
        };
        PopulateModuleHeader(model, license.Value!, "encomendas");
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

    private async Task<List<DeliveryRowViewModel>> GetDeliveries(Guid licenseId)
    {
        return await GetFromApi<List<DeliveryRowViewModel>>($"api/access/licenses/{licenseId}/deliveries") ?? new();
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
        var result = await PostToApiResult(path, payload, successMessage, errorMessage, includeApiKey);
        if (result.Success)
            TempData["SuccessMessage"] = result.Message;
        else
            TempData["ErrorMessage"] = result.Message;
    }

    private async Task<(bool Success, string Message)> PostToApiResult(string path, object payload, string successMessage, string errorMessage, bool includeApiKey = false)
    {
        if (!TryCreateAuthorizedClient(out var client))
            return (false, "Sessao expirada. Faca login novamente.");

        if (includeApiKey)
            client.DefaultRequestHeaders.Add("X-API-Key", GetApiKey());

        var response = await client.PostAsJsonAsync(BuildApiUrl(path), payload);
        if (response.IsSuccessStatusCode)
            return (true, successMessage);

        return (false, await ReadApiError(response, errorMessage));
    }

    private async Task PatchToApi(string path, object payload, string successMessage, string errorMessage)
    {
        if (!TryCreateAuthorizedClient(out var client))
        {
            TempData["ErrorMessage"] = "Sessao expirada. Faca login novamente.";
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Patch, BuildApiUrl(path))
        {
            Content = JsonContent.Create(payload)
        };

        var response = await client.SendAsync(request);
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

    private bool TryGetEnterpriseIdFromToken(out Guid enterpriseId)
    {
        enterpriseId = Guid.Empty;

        if (!Request.Cookies.TryGetValue("AuthToken", out var token) || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length < 2)
            return false;

        try
        {
            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return json.RootElement.TryGetProperty("enterprise_id", out var claim) &&
                   Guid.TryParse(claim.GetString(), out enterpriseId);
        }
        catch
        {
            return false;
        }
    }

    private string BuildApiUrl(string path)
    {
        var baseUrl = _configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:7118";
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
        if (string.IsNullOrWhiteSpace(body))
            return fallback;

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("Errors", out var errors))
                return FormatApiError(errors, fallback);

            if (json.RootElement.TryGetProperty("errors", out var modelErrors))
                return FormatApiError(modelErrors, fallback);

            if (json.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? fallback;

            if (json.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString() ?? fallback;
        }
        catch
        {
            return SafeApiError(response.StatusCode, fallback);
        }

        return SafeApiError(response.StatusCode, fallback);
    }

    private static string SafeApiError(System.Net.HttpStatusCode statusCode, string fallback) => statusCode switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Sua sessao expirou. Entre novamente.",
        System.Net.HttpStatusCode.Forbidden => "Voce nao tem permissao para realizar esta operacao.",
        System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout =>
            "Nao foi possivel comunicar com o equipamento ou servico. Verifique a conexao e tente novamente.",
        >= System.Net.HttpStatusCode.InternalServerError => "Ocorreu um erro interno na API. Tente novamente em instantes.",
        _ => fallback
    };

    private static string FormatApiError(JsonElement element, string fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? fallback : element.GetString()!,
            JsonValueKind.Array => string.Join("; ", element.EnumerateArray().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))),
            JsonValueKind.Object => string.Join("; ", element.EnumerateObject().SelectMany(x =>
                x.Value.ValueKind == JsonValueKind.Array
                    ? x.Value.EnumerateArray().Select(v => $"{x.Name}: {v}")
                    : new[] { $"{x.Name}: {x.Value}" }).Where(x => !string.IsNullOrWhiteSpace(x))),
            _ => fallback
        };
    }

    private static string FormatModelStateErrors(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
    {
        var errors = modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                string.IsNullOrWhiteSpace(entry.Key)
                    ? error.ErrorMessage
                    : $"{entry.Key}: {error.ErrorMessage}"))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();

        return errors.Count == 0
            ? "Revise os campos destacados e tente novamente."
            : string.Join("; ", errors);
    }

    private static async Task<Guid?> TryReadCreatedLicenseId(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            if (json.RootElement.TryGetProperty("license", out var license) &&
                license.TryGetProperty("id", out var id) &&
                Guid.TryParse(id.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string OnlyDigits(string value)
    {
        return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
    }

    private static string? OptionalString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ResolveAccessDeviceModel(int type)
    {
        return type >= 0 && type < AccessDeviceTypes.Length
            ? AccessDeviceTypes[type]
            : string.Empty;
    }
}
