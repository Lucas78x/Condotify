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
        var version = await DigestGetTextAsync(device, "/cgi-bin/magicBox.cgi?action=getSoftwareVersion");
        stopwatch.Stop();
        if (string.IsNullOrWhiteSpace(version))
            return DeviceInspectionResult.Unavailable("O terminal Intelbras nao respondeu ao diagnostico.");

        return new DeviceInspectionResult(
            true,
            (int)stopwatch.ElapsedMilliseconds,
            "Terminal online. Portas sugeridas pelo perfil da linha facial.",
            version.Trim(),
            "{}",
            [
                new DevicePortalCapability(1, "Porta 1", AccessRouteDirectionEnum.Entry, false),
                new DevicePortalCapability(2, "Porta 2", AccessRouteDirectionEnum.Entry, false)
            ]);
    }

    public Task<string> GetUsersAsync(AccessControlDevice device)
        => throw new NotImplementedException();

    public Task<bool> AddUserAsync(AccessControlDevice device, object user)
        => throw new NotImplementedException();

    public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
        => throw new NotImplementedException();

    public Task<string> GetEventsAsync(AccessControlDevice device)
        => throw new NotImplementedException();

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
                    ValidFrom = request.ValidFrom.ToString("yyyy-MM-dd HH:mm:ss"),
                    ValidTo = request.ValidTo.ToString("yyyy-MM-dd HH:mm:ss"),
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
                    ValidFrom = request.ValidFrom.ToString("yyyy-MM-dd HH:mm:ss"),
                    ValidTo = (isActive ? request.ValidTo : DateTime.UtcNow.AddSeconds(-1)).ToString("yyyy-MM-dd HH:mm:ss")
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
    private static string BaseAddress(AccessControlDevice device) =>
        $"http://{device.IPAddress}{(device.Port is <= 0 or 80 ? string.Empty : $":{device.Port}")}";
}
