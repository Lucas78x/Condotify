using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Extensions;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public class IntelbrasAccessControlDriver : IAccessControlDriver
{
    private readonly IHttpClientFactory _clientFactory;

    public bool Supports(DeviceTypeEnum type) => type.IsInIntelbras();

    public IntelbrasAccessControlDriver(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device)
    {
        var address = device.Port is <= 0 or 80 ? device.IPAddress : $"{device.IPAddress}:{device.Port}";
        var url = $"http://{address}/cgi-bin/configManager.cgi?action=getConfig&name=Network";

        var client = _clientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var byteArray = System.Text.Encoding.UTF8.GetBytes($"{device.Username}:{device.Password}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> OpenDoorAsync(AccessControlDevice device, int channel)
    {
        var url = $"{BaseAddress(device)}/cgi-bin/accessControl.cgi?action=openDoor&channel={channel}";
        var credentials = new CredentialCache();
        credentials.Add(new Uri(url), "Digest", new NetworkCredential(device.Username, device.Password));

        using var handler = new HttpClientHandler { Credentials = credentials, PreAuthenticate = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        using var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode;
    }

    public async Task<DeviceInspectionResult> InspectAsync(AccessControlDevice device)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var versionTask = DigestGetTextAsync(device, "/cgi-bin/magicBox.cgi?action=getSoftwareVersion");
        var serialTask = DigestGetTextAsync(device, "/cgi-bin/magicBox.cgi?action=getSerialNo");
        var interfacesTask = DigestGetTextAsync(device, "/cgi-bin/netApp.cgi?action=getInterfaces");
        await Task.WhenAll(versionTask, serialTask, interfacesTask);
        var version = versionTask.Result;
        stopwatch.Stop();
        if (string.IsNullOrWhiteSpace(version))
            return DeviceInspectionResult.Unavailable("O terminal Intelbras nao respondeu ao diagnostico.");

        var firmware = ResponseValue(version, "version", "softwareVersion") ?? version.Trim();
        var serialNumber = ResponseValue(serialTask.Result, "sn", "serial", "serialNo");
        var macAddress = ResponseValue(interfacesTask.Result, "PhysicalAddress", "MACAddress", "mac");
        return new DeviceInspectionResult(
            true,
            (int)stopwatch.ElapsedMilliseconds,
            "Terminal online. Portas sugeridas pelo perfil da linha facial.",
            firmware,
            "{}",
            [
                new DevicePortalCapability(1, "Porta 1", AccessRouteDirectionEnum.Entry, false),
                new DevicePortalCapability(2, "Porta 2", AccessRouteDirectionEnum.Entry, false)
            ],
            serialNumber,
            macAddress);
    }

    public async Task<string> GetUsersAsync(AccessControlDevice device) =>
        JsonSerializer.Serialize((await ReadCredentialInventoryAsync(device)).Items);

    public async Task<DeviceCredentialInventoryResult> ReadCredentialInventoryAsync(AccessControlDevice device)
    {
        var start = await DigestGetTextAsync(device, "/cgi-bin/AccessUser.cgi?action=startFind");
        if (string.IsNullOrWhiteSpace(start))
            return DeviceCredentialInventoryResult.Unavailable("O terminal Intelbras nao iniciou a leitura de usuarios.");
        try
        {
            var body = await DigestGetTextAsync(device, "/cgi-bin/AccessUser.cgi?action=doFind&Count=500");
            await DigestGetTextAsync(device, "/cgi-bin/AccessUser.cgi?action=stopFind");
            if (string.IsNullOrWhiteSpace(body)) return new(true, "Inventario remoto vazio.", []);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var values = root.TryGetProperty("Info", out var info) ? info
                : root.TryGetProperty("UserList", out var list) ? list : default;
            if (values.ValueKind != JsonValueKind.Array) return new(true, "Inventario remoto vazio.", []);
            var items = values.EnumerateArray().Select(value =>
            {
                var userId = JsonText(value, "UserID");
                var active = !value.TryGetProperty("IsValid", out var valid) || valid.ValueKind != JsonValueKind.False;
                return new DeviceCredentialInventoryItem($"users:{userId}", userId, string.Empty, null, string.Empty,
                    JsonText(value, "UserName"), active, value.GetRawText());
            }).Where(x => !string.IsNullOrWhiteSpace(x.ExternalUserId)).ToList();
            return new(true, $"{items.Count} usuario(s) lido(s) da Intelbras.", items);
        }
        catch (Exception exception)
        {
            return DeviceCredentialInventoryResult.Unavailable($"Resposta de inventario Intelbras invalida: {exception.Message}");
        }
    }

    public Task<bool> AddUserAsync(AccessControlDevice device, object user) =>
        Task.FromResult(false);

    public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId) =>
        DigestGetAsync(device, $"/cgi-bin/AccessUser.cgi?action=removeMulti&UserIDList[0]={Uri.EscapeDataString(userId)}");

    public async Task<string> GetEventsAsync(AccessControlDevice device) =>
        JsonSerializer.Serialize(await GetAccessEventsAsync(device, 200));

    public async Task<CredentialOperationResult> UpsertCredentialAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request)
    {
        var userId = string.IsNullOrWhiteSpace(request.ExternalUserId) ? request.Registration : request.ExternalUserId;
        var configuredDoors = request.Portals?.Select(x => x.PortalNumber).Distinct().OrderBy(x => x).ToArray();
        var userPayload = new
        {
            UserList = new[]
            {
                new
                {
                    UserID = userId,
                    UserName = request.ResidentName,
                    UserType = 0,
                    IsValid = request.IsActive,
                    ValidFrom = AccessControlDeviceTimeZone.FormatLocal(request.ValidFrom, "yyyy-MM-dd HH:mm:ss"),
                    ValidTo = AccessControlDeviceTimeZone.FormatLocal(request.ValidTo, "yyyy-MM-dd HH:mm:ss"),
                    Doors = configuredDoors is { Length: > 0 } ? configuredDoors : new[] { 0 },
                    TimeSections = new[] { 255 }
                }
            }
        };

        var action = string.IsNullOrWhiteSpace(request.ExternalUserId) ? "insertMulti" : "updateMulti";
        if (!await DigestPostJsonAsync(device, $"/cgi-bin/AccessUser.cgi?action={action}", userPayload))
            return CredentialOperationResult.Fail("O equipamento Intelbras recusou o cadastro do usuario.");

        if (request.Type == AccessCredentialTypeEnum.Face)
        {
            if (string.IsNullOrWhiteSpace(request.ImageBase64))
                return new CredentialOperationResult(false, userId, "face", "Usuario vinculado; envie uma foto para concluir o facial.");

            var facePayload = new
            {
                UserID = userId,
                Info = new { UserName = request.ResidentName, PhotoData = new[] { NormalizeBase64(request.ImageBase64) } }
            };
            var faceAction = string.IsNullOrWhiteSpace(request.ExternalCredentialId) ? "add" : "update";
            var faceOk = await DigestPostJsonAsync(device, $"/cgi-bin/FaceInfoManager.cgi?action={faceAction}", facePayload);
            return faceOk
                ? CredentialOperationResult.Ok(userId, "face", "Facial sincronizado com o equipamento.")
                : CredentialOperationResult.Fail("O usuario foi criado, mas a imagem facial foi recusada. Verifique formato, resolucao e limite de 100 KB.");
        }

        if (request.Type is AccessCredentialTypeEnum.Card or AccessCredentialTypeEnum.Tag or AccessCredentialTypeEnum.VehicleTag)
        {
            var cardPayload = new
            {
                CardList = new[]
                {
                    new { CardNo = request.Identifier.Trim(), UserID = userId, CardType = 0, CardStatus = request.IsActive ? 0 : 1 }
                }
            };
            var cardOk = await DigestPostJsonAsync(device, "/cgi-bin/AccessCard.cgi?action=insertMulti", cardPayload);
            return cardOk
                ? CredentialOperationResult.Ok(userId, request.Identifier.Trim(), "Cartao ou tag sincronizado com o equipamento.")
                : CredentialOperationResult.Fail("O equipamento recusou o cartao ou tag. Verifique se o numero ja esta cadastrado.");
        }

        if (request.Type == AccessCredentialTypeEnum.Password)
        {
            var passwordUrl = $"/cgi-bin/recordUpdater.cgi?action=insert&name=AccessControlCard&CardName={Uri.EscapeDataString(request.ResidentName)}&UserID={Uri.EscapeDataString(userId!)}&Password={Uri.EscapeDataString(request.Identifier)}&CardStatus={(request.IsActive ? 0 : 1)}";
            return await DigestGetAsync(device, passwordUrl)
                ? CredentialOperationResult.Ok(userId, "password", "Senha sincronizada com o equipamento.")
                : CredentialOperationResult.Fail("O equipamento recusou a senha de acesso.");
        }

        return CredentialOperationResult.Fail("Este tipo de credencial nao e suportado pela linha facial Intelbras.");
    }

    public async Task<CredentialOperationResult> SetCredentialActiveAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalUserId))
            return CredentialOperationResult.Fail("A credencial ainda nao possui usuario vinculado neste equipamento.");

        var payload = new
        {
            UserList = new[]
            {
                new
                {
                    UserID = request.ExternalUserId,
                    IsValid = isActive,
                    ValidFrom = AccessControlDeviceTimeZone.FormatLocal(request.ValidFrom, "yyyy-MM-dd HH:mm:ss"),
                    ValidTo = AccessControlDeviceTimeZone.FormatLocal(
                        isActive ? request.ValidTo : DateTime.UtcNow.AddSeconds(-1),
                        "yyyy-MM-dd HH:mm:ss")
                }
            }
        };
        var success = await DigestPostJsonAsync(device, "/cgi-bin/AccessUser.cgi?action=updateMulti", payload);
        return success
            ? CredentialOperationResult.Ok(request.ExternalUserId, request.ExternalCredentialId, isActive ? "Credencial ativada." : "Credencial suspensa.")
            : CredentialOperationResult.Fail("O equipamento Intelbras nao confirmou a alteracao de status.");
    }

    public async Task<CredentialOperationResult> RemoveCredentialAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalUserId))
            return CredentialOperationResult.Fail("A credencial nao possui vinculo externo neste equipamento.");

        var path = request.Type switch
        {
            AccessCredentialTypeEnum.Face => $"/cgi-bin/FaceInfoManager.cgi?action=remove&UserID={Uri.EscapeDataString(request.ExternalUserId)}",
            AccessCredentialTypeEnum.Card or AccessCredentialTypeEnum.Tag or AccessCredentialTypeEnum.VehicleTag => $"/cgi-bin/AccessCard.cgi?action=removeMulti&CardNoList[0]={Uri.EscapeDataString(request.Identifier)}",
            _ => $"/cgi-bin/AccessUser.cgi?action=removeMulti&UserIDList[0]={Uri.EscapeDataString(request.ExternalUserId)}"
        };
        var success = await DigestGetAsync(device, path);
        return success
            ? CredentialOperationResult.Ok(request.ExternalUserId, request.ExternalCredentialId, "Credencial removida do equipamento.")
            : CredentialOperationResult.Fail("O equipamento Intelbras nao confirmou a remocao.");
    }

    public Task<CredentialOperationResult> StartFaceEnrollmentAsync(AccessControlDevice device, string externalUserId) =>
        Task.FromResult(CredentialOperationResult.Fail("A linha facial Intelbras exige o envio de uma foto pelo portal para cadastro remoto."));

    public Task<CredentialOperationResult> CancelFaceEnrollmentAsync(AccessControlDevice device) =>
        Task.FromResult(CredentialOperationResult.Fail("Nao ha captura facial remota ativa para este driver Intelbras."));

    public async Task<IReadOnlyList<DeviceAccessEvent>> GetAccessEventsAsync(AccessControlDevice device, int take)
    {
        var body = await DigestGetTextAsync(device, $"/cgi-bin/recordFinder.cgi?action=find&name=AccessControlCardRec&condition.count={Math.Clamp(take, 1, 200)}");
        if (string.IsNullOrWhiteSpace(body)) return [];

        var records = new Dictionary<int, Dictionary<string, string>>();
        foreach (var line in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line, @"^records\[(\d+)\]\.([^=]+)=(.*)$");
            if (!match.Success) continue;
            var index = int.Parse(match.Groups[1].Value);
            if (!records.TryGetValue(index, out var values)) records[index] = values = new();
            values[match.Groups[2].Value] = match.Groups[3].Value;
        }

        return records.Select(pair =>
        {
            var value = pair.Value;
            _ = DateTime.TryParse(value.GetValueOrDefault("CreateTime"), out var occurredAt);
            var status = value.GetValueOrDefault("Status");
            return new DeviceAccessEvent(
                value.GetValueOrDefault("RecNo") ?? pair.Key.ToString(),
                status == "1" ? "Acesso autorizado" : "Acesso negado",
                status == "1",
                occurredAt == default ? DateTime.UtcNow : occurredAt,
                value.GetValueOrDefault("UserID"),
                value.GetValueOrDefault("CardName"),
                value.GetValueOrDefault("CardNo"),
                value.GetValueOrDefault("Door"),
                string.Join(" | ", value.Where(x => x.Key is "Method" or "Type" or "ErrorCode").Select(x => $"{x.Key}: {x.Value}")));
        }).OrderByDescending(x => x.OccurredAt).Take(take).ToList();
    }

    private async Task<bool> DigestPostJsonAsync(AccessControlDevice device, string path, object payload)
    {
        var url = $"{BaseAddress(device)}{path}";
        using var handler = CreateDigestHandler(url, device);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content);
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> DigestGetAsync(AccessControlDevice device, string path) =>
        !string.IsNullOrWhiteSpace(await DigestGetTextAsync(device, path));

    private async Task<string?> DigestGetTextAsync(AccessControlDevice device, string path)
    {
        var url = $"{BaseAddress(device)}{path}";
        using var handler = CreateDigestHandler(url, device);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
    }

    private static HttpClientHandler CreateDigestHandler(string url, AccessControlDevice device)
    {
        var credentials = new CredentialCache();
        credentials.Add(new Uri(url), "Digest", new NetworkCredential(device.Username, device.Password));
        return new HttpClientHandler { Credentials = credentials, PreAuthenticate = true };
    }

    private static string NormalizeBase64(string value) => value.Contains(',') ? value[(value.IndexOf(',') + 1)..] : value;
    private static string JsonText(JsonElement value, string name) => value.TryGetProperty(name, out var property) ? property.ToString() : string.Empty;
    private static string? ResponseValue(string? response, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        foreach (var line in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            if (!keys.Any(candidate => key.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                                       key.EndsWith($".{candidate}", StringComparison.OrdinalIgnoreCase)))
                continue;
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
    private static string BaseAddress(AccessControlDevice device) =>
        $"http://{device.IPAddress}{(device.Port is <= 0 or 80 ? string.Empty : $":{device.Port}")}";
}
