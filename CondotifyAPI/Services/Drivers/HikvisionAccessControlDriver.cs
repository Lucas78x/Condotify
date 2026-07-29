using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Extensions;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

/// <summary>
/// Driver ISAPI para terminais faciais Hikvision MinMoe, incluindo as familias
/// DS-K1T673, DS-K1T671 e DS-K1T323.
///
/// Operacoes implementadas:
/// - teste e inspecao do equipamento;
/// - abertura remota de porta;
/// - cadastro, alteracao, consulta, ativacao e exclusao de pessoas;
/// - cadastro, alteracao e exclusao de faces via multipart/form-data;
/// - cadastro, alteracao e exclusao de cartoes/tags;
/// - senha de acesso quando suportada pelo firmware;
/// - inventario paginado de pessoas;
/// - consulta paginada de eventos de acesso;
/// - deteccao de capacidades e fallbacks entre firmwares MinMoe.
/// </summary>
public sealed class HikvisionAccessControlDriver : IAccessControlDriver
{
    private const int DefaultTimeoutSeconds = 30;
    private const int SearchPageSize = 30;
    private const string DefaultFaceLibrary = "1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public HikvisionAccessControlDriver(IHttpClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
    }

    public bool Supports(DeviceTypeEnum type) => type.IsInHikvision();

    public async Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device)
    {
        try
        {
            var address = device.Port is <= 0 or 80
                ? device.IPAddress
                : $"{device.IPAddress}:{device.Port}";

            var baseUri = new Uri($"http://{address}");
            var credentials = new CredentialCache();
            credentials.Add(
                baseUri,
                "Digest",
                new NetworkCredential(device.Username, device.Password));

            using var handler = new HttpClientHandler
            {
                Credentials = credentials,
                PreAuthenticate = false,
                UseDefaultCredentials = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            using var jsonResponse = await client.GetAsync(
                new Uri(baseUri, "/ISAPI/System/deviceInfo?format=json"));

            if (jsonResponse.IsSuccessStatusCode)
                return true;

            // Equipamentos/firmwares antigos podem responder somente XML.
            using var xmlResponse = await client.GetAsync(
                new Uri(baseUri, "/ISAPI/System/deviceInfo"));

            return xmlResponse.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    public async Task<bool> OpenDoorAsync(AccessControlDevice device, int channel)
    {
        channel = Math.Max(1, channel);

        var jsonPayload = new
        {
            RemoteControlDoor = new
            {
                cmd = "open"
            }
        };

        var jsonResult = await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            $"/ISAPI/AccessControl/RemoteControl/door/{channel}?format=json",
            jsonPayload);

        if (jsonResult.Success)
            return true;

        // Fallback XML para firmware antigo.
        const string xml = "<RemoteControlDoor><cmd>open</cmd></RemoteControlDoor>";
        var xmlResult = await SendTextForResultAsync(
            device,
            HttpMethod.Put,
            $"/ISAPI/AccessControl/RemoteControl/door/{channel}",
            xml,
            "application/xml");

        return xmlResult.Success;
    }

    public async Task<DeviceInspectionResult> InspectAsync(AccessControlDevice device)
    {
        var stopwatch = Stopwatch.StartNew();
        var deviceInfo = await GetDeviceInfoAsync(device);
        stopwatch.Stop();

        if (!deviceInfo.Success)
        {
            return DeviceInspectionResult.Unavailable(
                $"O terminal Hikvision nao respondeu ao diagnostico ISAPI. {deviceInfo.ErrorMessage}".Trim());
        }

        var capabilities = await ReadCapabilitiesAsync(device);
        var model = FirstNotEmpty(
            ReadJsonString(deviceInfo.Body, "DeviceInfo", "model"),
            ReadXmlValue(deviceInfo.Body, "model"),
            "Hikvision MinMoe");
        var firmware = FirstNotEmpty(
            ReadJsonString(deviceInfo.Body, "DeviceInfo", "firmwareVersion"),
            ReadXmlValue(deviceInfo.Body, "firmwareVersion"),
            "Versao nao informada");
        var serial = FirstNotEmpty(
            ReadJsonString(deviceInfo.Body, "DeviceInfo", "serialNumber"),
            ReadXmlValue(deviceInfo.Body, "serialNumber"));
        var macAddress = FirstNotEmpty(
            ReadJsonString(deviceInfo.Body, "DeviceInfo", "macAddress"),
            ReadXmlValue(deviceInfo.Body, "macAddress"));

        var raw = JsonSerializer.Serialize(new
        {
            model,
            firmware,
            serial,
            macAddress,
            capabilities
        }, JsonOptions);

        return new DeviceInspectionResult(
            true,
            (int)stopwatch.ElapsedMilliseconds,
            $"Terminal {model} online via ISAPI. Usuario: {capabilities.Users}; Face: {capabilities.Face}; Cartao: {capabilities.Card}; Eventos: {capabilities.Events}.",
            firmware,
            raw,
            [
                new DevicePortalCapability(1, "Porta 1", AccessRouteDirectionEnum.Entry, false)
            ],
            serial,
            macAddress);
    }

    public async Task<string> GetUsersAsync(AccessControlDevice device) =>
        JsonSerializer.Serialize((await ReadCredentialInventoryAsync(device)).Items, JsonOptions);

    public async Task<DeviceCredentialInventoryResult> ReadCredentialInventoryAsync(AccessControlDevice device)
    {
        try
        {
            var users = await SearchAllUsersAsync(device);
            if (!users.Success)
                return DeviceCredentialInventoryResult.Unavailable(users.ErrorMessage ?? "Falha ao consultar usuarios Hikvision.");

            var items = new List<DeviceCredentialInventoryItem>();
            foreach (var user in users.Items)
            {
                var employeeNo = JsonText(user, "employeeNo");
                if (string.IsNullOrWhiteSpace(employeeNo))
                    continue;

                var name = JsonText(user, "name");
                var enabled = ReadUserEnabled(user);

                items.Add(new DeviceCredentialInventoryItem(
                RemoteKey: $"users:{employeeNo}",
                ExternalUserId: employeeNo,
                ExternalCredentialId: string.Empty,
                Type: AccessCredentialTypeEnum.Face,
                Identifier: string.Empty,
                PersonName: name,
                Active: enabled,
                RawJson: user.GetRawText()));
            }

            return new DeviceCredentialInventoryResult(
                true,
                $"{items.Count} usuario(s) lido(s) do terminal Hikvision.",
                items);
        }
        catch (Exception exception)
        {
            return DeviceCredentialInventoryResult.Unavailable(
                $"Resposta de inventario Hikvision invalida: {exception.Message}");
        }
    }

    public async Task<bool> AddUserAsync(AccessControlDevice device, object user)
    {
        // Mantem compatibilidade com chamadas genericas existentes no projeto.
        // O objeto deve representar UserInfo ou conter uma propriedade UserInfo.
        if (user is null)
            return false;

        var node = JsonSerializer.SerializeToNode(user, JsonOptions);
        if (node is null)
            return false;

        JsonNode payload;
        if (node is JsonObject objectNode &&
            objectNode.TryGetPropertyValue("UserInfo", out var existingUserInfo) &&
            existingUserInfo is not null)
        {
            payload = objectNode;
        }
        else
        {
            payload = new JsonObject
            {
                ["UserInfo"] = node.DeepClone()
            };
        }

        var result = await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            "/ISAPI/AccessControl/UserInfo/SetUp?format=json",
            payload);

        return result.Success;
    }

    public async Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        userId = NormalizeEmployeeNo(userId);

        var payload = new
        {
            UserInfoDetail = new
            {
                mode = "byEmployeeNo",
                EmployeeNoList = new[]
                {
                    new { employeeNo = userId }
                }
            }
        };

        var result = await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            "/ISAPI/AccessControl/UserInfoDetail/Delete?format=json",
            payload);

        if (result.Success)
            return true;

        // Alguns firmwares aceitam DELETE no mesmo recurso.
        result = await SendJsonForResultAsync(
            device,
            HttpMethod.Delete,
            "/ISAPI/AccessControl/UserInfoDetail/Delete?format=json",
            payload);

        return result.Success;
    }

    public async Task<string> GetEventsAsync(AccessControlDevice device) =>
        JsonSerializer.Serialize(await GetAccessEventsAsync(device, 200), JsonOptions);

    public async Task<CredentialOperationResult> UpsertCredentialAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request)
    {
        var employeeNo = FirstNotEmpty(request.ExternalUserId, request.Registration);
        if (string.IsNullOrWhiteSpace(employeeNo))
            return CredentialOperationResult.Fail("Informe a matricula ou o identificador externo do usuario.");

        employeeNo = NormalizeEmployeeNo(employeeNo);

        var userResult = await UpsertUserAsync(device, request, employeeNo);
        if (!userResult.Success)
            return CredentialOperationResult.Fail(userResult.ErrorMessage ?? "O terminal recusou o cadastro da pessoa.");

        switch (request.Type)
        {
            case AccessCredentialTypeEnum.Face:
                {
                    if (string.IsNullOrWhiteSpace(request.ImageBase64))
                    {
                        return new CredentialOperationResult(
                            false,
                            employeeNo,
                            "face",
                            "A pessoa foi sincronizada, mas ainda falta enviar a fotografia facial.");
                    }

                    var faceResult = await UpsertFaceAsync(device, employeeNo, request.ImageBase64);
                    return faceResult.Success
                        ? CredentialOperationResult.Ok(employeeNo, "face", "Pessoa e fotografia facial sincronizadas com o terminal Hikvision.")
                        : new CredentialOperationResult(
                            false,
                            employeeNo,
                            "face",
                            $"A pessoa foi sincronizada, mas a fotografia foi recusada: {faceResult.ErrorMessage}");
                }

            case AccessCredentialTypeEnum.Card:
            case AccessCredentialTypeEnum.Tag:
            case AccessCredentialTypeEnum.VehicleTag:
                {
                    if (string.IsNullOrWhiteSpace(request.Identifier))
                        return CredentialOperationResult.Fail("Informe o numero do cartao ou tag.");

                    var cardResult = await UpsertCardAsync(
                        device,
                        employeeNo,
                        request.Identifier.Trim(),
                        request.IsActive);

                    return cardResult.Success
                        ? CredentialOperationResult.Ok(employeeNo, request.Identifier.Trim(), "Pessoa e cartao/tag sincronizados com o terminal Hikvision.")
                        : CredentialOperationResult.Fail($"A pessoa foi sincronizada, mas o cartao/tag foi recusado: {cardResult.ErrorMessage}");
                }

            case AccessCredentialTypeEnum.Password:
                {
                    if (string.IsNullOrWhiteSpace(request.Identifier))
                        return CredentialOperationResult.Fail("Informe a senha de acesso.");

                    var passwordResult = await SetPasswordAsync(device, request, employeeNo);
                    return passwordResult.Success
                        ? CredentialOperationResult.Ok(employeeNo, "password", "Pessoa e senha sincronizadas com o terminal Hikvision.")
                        : CredentialOperationResult.Fail($"A pessoa foi sincronizada, mas a senha foi recusada: {passwordResult.ErrorMessage}");
                }

            default:
                return CredentialOperationResult.Ok(employeeNo, null, "Pessoa sincronizada com o terminal Hikvision.");
        }
    }

    public async Task<CredentialOperationResult> SetCredentialActiveAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request,
        bool isActive)
    {
        var employeeNo = FirstNotEmpty(
            request.ExternalUserId,
            request.Registration);

        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            return CredentialOperationResult.Fail(
                "A credencial nao possui usuario externo vinculado ao terminal.");
        }

        employeeNo = NormalizeEmployeeNo(employeeNo);

        var userResult = await UpsertUserAsync(
            device,
            request,
            employeeNo,
            isActive);

        if (!userResult.Success)
        {
            return CredentialOperationResult.Fail(
                userResult.ErrorMessage ??
                "O terminal nao confirmou a alteracao de status do usuario.");
        }

        // Cartoes e tags precisam ser recriados ao restaurar e removidos ao
        // suspender. Apenas alterar Valid.enable do usuario nao restaura um
        // cartao que tenha sido excluido anteriormente.
        if (request.Type is AccessCredentialTypeEnum.Card or
            AccessCredentialTypeEnum.Tag or
            AccessCredentialTypeEnum.VehicleTag)
        {
            if (string.IsNullOrWhiteSpace(request.Identifier))
            {
                return CredentialOperationResult.Fail(
                    "O usuario foi atualizado, mas o numero do cartao/tag nao foi informado.");
            }

            var cardResult = isActive
                ? await UpsertCardAsync(device, employeeNo, request.Identifier, true)
                : await DeleteCardAsync(device, request.Identifier.Trim());

            if (!cardResult.Success)
            {
                return CredentialOperationResult.Fail(
                    cardResult.ErrorMessage ??
                    "O terminal nao confirmou a alteracao do cartao/tag.");
            }
        }

        return CredentialOperationResult.Ok(
            employeeNo,
            request.ExternalCredentialId,
            isActive
                ? "Credencial restaurada e ativada no terminal Hikvision."
                : "Credencial suspensa no terminal Hikvision.");
    }

    public async Task<CredentialOperationResult> RemoveCredentialAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request)
    {
        var employeeNo = FirstNotEmpty(request.ExternalUserId, request.Registration);
        if (string.IsNullOrWhiteSpace(employeeNo))
            return CredentialOperationResult.Fail("A credencial nao possui usuario externo vinculado ao terminal.");

        employeeNo = NormalizeEmployeeNo(employeeNo);
        IsapiResult result;

        switch (request.Type)
        {
            case AccessCredentialTypeEnum.Face:
                result = await DeleteFaceAsync(device, employeeNo);
                break;

            case AccessCredentialTypeEnum.Card:
            case AccessCredentialTypeEnum.Tag:
            case AccessCredentialTypeEnum.VehicleTag:
                if (string.IsNullOrWhiteSpace(request.Identifier))
                    return CredentialOperationResult.Fail("Informe o numero do cartao/tag que sera removido.");
                result = await DeleteCardAsync(device, request.Identifier.Trim());
                break;

            default:
                return await DeleteUserAsync(device, employeeNo)
                    ? CredentialOperationResult.Ok(employeeNo, request.ExternalCredentialId, "Pessoa e credenciais removidas do terminal Hikvision.")
                    : CredentialOperationResult.Fail("O terminal Hikvision nao confirmou a remocao da pessoa.");
        }

        return result.Success
            ? CredentialOperationResult.Ok(employeeNo, request.ExternalCredentialId, "Credencial removida do terminal Hikvision.")
            : CredentialOperationResult.Fail(result.ErrorMessage ?? "O terminal Hikvision nao confirmou a remocao.");
    }

    public Task<CredentialOperationResult> StartFaceEnrollmentAsync(
        AccessControlDevice device,
        string externalUserId) =>
        Task.FromResult(CredentialOperationResult.Fail(
            "Nos modelos DS-K1T673, DS-K1T671 e DS-K1T323, o cadastro remoto mais compativel e realizado enviando uma fotografia pelo portal. A captura local deve ser iniciada no proprio terminal."));

    public Task<CredentialOperationResult> CancelFaceEnrollmentAsync(AccessControlDevice device) =>
        Task.FromResult(CredentialOperationResult.Fail(
            "Nao existe captura facial remota iniciada por este driver."));

    public async Task<IReadOnlyList<DeviceAccessEvent>> GetAccessEventsAsync(
        AccessControlDevice device,
        int take)
    {
        take = Math.Clamp(take, 1, 1000);
        var events = new List<DeviceAccessEvent>();
        var searchId = Guid.NewGuid().ToString("N");
        var position = 0;
        var pageSize = Math.Min(30, take);
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        while (events.Count < take)
        {
            var requestedPageSize = Math.Min(pageSize, take - events.Count);

            var payload = new
            {
                AcsEventCond = new
                {
                    searchID = searchId,
                    searchResultPosition = position,
                    maxResults = requestedPageSize,
                    major = 0,
                    minor = 0,
                    startTime = FormatIsapiDate(from),
                    endTime = FormatIsapiDate(to),
                    timeReverseOrder = true,
                    picEnable = false
                }
            };

            var response = await SendJsonForResultAsync(
                device,
                HttpMethod.Post,
                "/ISAPI/AccessControl/AcsEvent?format=json",
                payload);

            if (!response.Success || string.IsNullOrWhiteSpace(response.Body))
                break;

            if (!LooksLikeJson(response.Body))
                break;

            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            var info = TryGetPropertyIgnoreCase(root, "AcsEvent") ?? root;
            var list = TryGetPropertyIgnoreCase(info, "InfoList");
            if (list is null || list.Value.ValueKind != JsonValueKind.Array)
                break;

            var count = 0;
            foreach (var item in list.Value.EnumerateArray())
            {
                count++;
                var serial = FirstNotEmpty(
                    JsonText(item, "serialNo"),
                    JsonText(item, "eventID"),
                    $"{position + count}");
                var employeeNo = FirstNotEmpty(
                    JsonText(item, "employeeNoString"),
                    JsonText(item, "employeeNo"));
                var name = JsonText(item, "name");
                var cardNo = JsonText(item, "cardNo");
                var doorNo = JsonText(item, "doorNo");
                var currentVerifyMode = JsonText(item, "currentVerifyMode");
                var attendanceStatus = JsonText(item, "attendanceStatus");
                var minor = JsonText(item, "minor");
                var eventTime = ParseIsapiDate(FirstNotEmpty(
                    JsonText(item, "time"),
                    JsonText(item, "dateTime"))) ?? DateTime.UtcNow;
                var allowed = DetermineAccessGranted(item);

                events.Add(new DeviceAccessEvent(
                    serial,
                    allowed ? "Acesso autorizado" : "Acesso negado",
                    allowed,
                    eventTime,
                    employeeNo,
                    name,
                    cardNo,
                    doorNo,
                    $"verifyMode: {currentVerifyMode} | attendance: {attendanceStatus} | minor: {minor}"));
            }

            if (count == 0)
                break;

            position += count;
            var totalMatches = JsonInt(info, "totalMatches");
            var status = JsonText(info, "responseStatusStrg");

            if (status.Equals("NO MATCH", StringComparison.OrdinalIgnoreCase) ||
                (totalMatches > 0 && position >= totalMatches) ||
                count < requestedPageSize)
            {
                break;
            }
        }

        return events
            .OrderByDescending(x => x.OccurredAt)
            .Take(take)
            .ToList();
    }

    private async Task<IsapiResult> UpsertUserAsync(
     AccessControlDevice device,
     CredentialProvisionRequest request,
     string employeeNo,
     bool? forceActive = null)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);

        var active = forceActive ?? request.IsActive;

        var doorRights = request.Portals?
            .Select(x => x.PortalNumber)
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (doorRights is not { Length: > 0 })
            doorRights = [1];

        /*
         * Os terminais Hikvision MinMoe normalmente trabalham com horário local
         * sem offset quando timeType = "local".
         */
        var nowLocal = DateTime.Now;

        var validFrom = NormalizeHikvisionBeginDate(
            request.ValidFrom,
            nowLocal.AddMinutes(-5));

        var validTo = NormalizeHikvisionEndDate(
            request.ValidTo,
            nowLocal.AddYears(10));

        if (active)
        {
            /*
             * Na restauração, não podemos enviar uma validade já vencida.
             * Caso a data armazenada tenha expirado, geramos uma nova validade.
             */
            if (validTo <= nowLocal)
                validTo = nowLocal.AddYears(10);

            if (validFrom >= validTo)
                validFrom = nowLocal.AddMinutes(-5);
        }
        else
        {
            /*
             * Alguns firmwares rejeitam enable=false com datas inválidas.
             * Mantemos datas coerentes e usamos enable para desativar.
             */
            validFrom = nowLocal.AddMinutes(-5);
            validTo = nowLocal.AddYears(10);
        }

        var rightPlans = new JsonArray();

        foreach (var door in doorRights)
        {
            rightPlans.Add(new JsonObject
            {
                ["doorNo"] = door,
                ["planTemplateNo"] = "1"
            });
        }

        var payload = BuildUserPayload(
            request,
            employeeNo,
            active,
            validFrom,
            validTo,
            doorRights,
            rightPlans,
            includeProfile: true,
            includeAccessRights: true);

        var result = await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            "/ISAPI/AccessControl/UserInfo/SetUp?format=json",
            payload);

        if (result.Success)
            return result;

        if (!IsBadJsonContent(result))
            return result;

        /*
         * O ISAPI separa inclusão (Record) de alteração (SetUp). Alguns
         * firmwares aceitam SetUp como upsert, outros devolvem badJsonContent.
         * Antes do fallback consultamos a pessoa para escolher a operação sem
         * apagar nem sobrescrever permissões já configuradas no equipamento.
         */
        var userExists = await UserExistsAsync(device, employeeNo);

        if (userExists == false)
        {
            return await SendJsonForResultAsync(
                device,
                HttpMethod.Post,
                "/ISAPI/AccessControl/UserInfo/Record?format=json",
                payload);
        }

        /*
         * Para uma pessoa existente, restaurar/suspender requer somente a
         * validade. doorRight e RightPlan formam um par no contrato ISAPI;
         * remover apenas RightPlan, como fazia o fallback anterior, gera um
         * JSON semanticamente inválido em diversos MinMoe.
         */
        var statusPayload = BuildUserPayload(
            request,
            employeeNo,
            active,
            validFrom,
            validTo,
            doorRights,
            rightPlans,
            includeProfile: false,
            includeAccessRights: false);

        var statusResult = await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            "/ISAPI/AccessControl/UserInfo/SetUp?format=json",
            statusPayload);

        if (statusResult.Success || userExists == true)
            return statusResult;

        // Se a consulta não foi conclusiva, tentamos a inclusão oficial.
        var recordResult = await SendJsonForResultAsync(
            device,
            HttpMethod.Post,
            "/ISAPI/AccessControl/UserInfo/Record?format=json",
            payload);

        if (!IsDuplicateUser(recordResult))
            return recordResult;

        return statusResult;
    }

    internal static JsonObject BuildUserPayload(
        CredentialProvisionRequest request,
        string employeeNo,
        bool active,
        DateTime validFrom,
        DateTime validTo,
        IReadOnlyList<int> doorRights,
        JsonArray rightPlans,
        bool includeProfile,
        bool includeAccessRights)
    {
        var userInfo = new JsonObject
        {
            ["employeeNo"] = employeeNo,
            ["userType"] = "normal",
            ["Valid"] = new JsonObject
            {
                ["enable"] = active,
                ["beginTime"] = FormatIsapiLocalDate(validFrom),
                ["endTime"] = FormatIsapiLocalDate(validTo),
                ["timeType"] = "local"
            }
        };

        if (includeProfile)
        {
            userInfo["name"] = TruncateUtf8(request.ResidentName, 32);

            if (request.Type == AccessCredentialTypeEnum.Password &&
                !string.IsNullOrWhiteSpace(request.Identifier))
            {
                userInfo["password"] = request.Identifier.Trim();
            }
        }

        if (includeAccessRights)
        {
            userInfo["doorRight"] = string.Join(",", doorRights);
            userInfo["RightPlan"] = rightPlans.DeepClone();
        }

        return new JsonObject { ["UserInfo"] = userInfo };
    }

    private async Task<bool?> UserExistsAsync(
        AccessControlDevice device,
        string employeeNo)
    {
        var payload = new
        {
            UserInfoSearchCond = new
            {
                searchID = Guid.NewGuid().ToString("N"),
                searchResultPosition = 0,
                maxResults = 1,
                EmployeeNoList = new[]
                {
                    new { employeeNo }
                }
            }
        };

        var result = await SendJsonForResultAsync(
            device,
            HttpMethod.Post,
            "/ISAPI/AccessControl/UserInfo/Search?format=json",
            payload);

        if (!result.Success || !LooksLikeJson(result.Body))
            return null;

        using var document = JsonDocument.Parse(result.Body!);
        var search = TryGetPropertyIgnoreCase(document.RootElement, "UserInfoSearch")
            ?? document.RootElement;
        var users = TryGetPropertyIgnoreCase(search, "UserInfo");

        if (users is not { ValueKind: JsonValueKind.Array })
            return false;

        return users.Value.EnumerateArray().Any(user =>
            JsonText(user, "employeeNo").Equals(employeeNo, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDuplicateUser(IsapiResult result)
    {
        var content = string.Join(" ", result.ErrorMessage, result.Body);

        return content.Contains("alreadyExist", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("deviceUserAlreadyExist", StringComparison.OrdinalIgnoreCase);
    }
    private static string FormatIsapiLocalDate(DateTime value)
    {
        /*
         * Hikvision MinMoe com timeType=local:
         * 2026-07-16T08:30:00
         *
         * Não enviar:
         * 2026-07-16T08:30:00-03:00
         * 2026-07-16T11:30:00+00:00
         */
        var localValue = value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
        };

        return localValue.ToString(
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture);
    }

    private static DateTime NormalizeHikvisionBeginDate(
        DateTime value,
        DateTime fallback)
    {
        if (value == default ||
            value.Year < 2000 ||
            value.Year > 2037)
        {
            return fallback;
        }

        return ToLocalUnspecified(value);
    }

    private static DateTime NormalizeHikvisionEndDate(
        DateTime value,
        DateTime fallback)
    {
        if (value == default ||
            value.Year < 2000 ||
            value.Year > 2037)
        {
            return fallback;
        }

        return ToLocalUnspecified(value);
    }

    private static DateTime ToLocalUnspecified(DateTime value)
    {
        var localValue = value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => value
        };

        return DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified);
    }

    private static bool IsBadJsonContent(IsapiResult result)
    {
        var content = string.Join(
            " ",
            result.ErrorMessage,
            result.Body);

        return content.Contains(
                   "badJsonContent",
                   StringComparison.OrdinalIgnoreCase) ||
               content.Contains(
                   "Invalid Content",
                   StringComparison.OrdinalIgnoreCase) ||
               content.Contains(
                   "1610612759",
                   StringComparison.OrdinalIgnoreCase);
    }

    private Task<IsapiResult> SetPasswordAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request,
        string employeeNo) =>
        UpsertUserAsync(device, request, employeeNo);

    private async Task<IsapiResult> UpsertCardAsync(
        AccessControlDevice device,
        string employeeNo,
        string cardNo,
        bool enabled)
    {
        employeeNo = NormalizeEmployeeNo(employeeNo);
        cardNo = cardNo.Trim();

        if (string.IsNullOrWhiteSpace(cardNo))
            return IsapiResult.Fail("O numero do cartao/tag nao foi informado.");

        // SetUp nao aceita o campo deleteCard em diversos firmwares MinMoe.
        // Para suspender uma credencial de cartao, excluimos somente o cartao.
        if (!enabled)
            return await DeleteCardAsync(device, cardNo);

        var payload = new
        {
            CardInfo = new
            {
                employeeNo,
                cardNo,
                cardType = "normalCard"
            }
        };

        return await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            "/ISAPI/AccessControl/CardInfo/SetUp?format=json",
            payload);
    }

    private async Task<IsapiResult> DeleteCardAsync(AccessControlDevice device, string cardNo)
    {
        var payload = new
        {
            CardInfoDelCond = new
            {
                CardNoList = new[]
                {
                    new { cardNo }
                }
            }
        };

        var result = await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            "/ISAPI/AccessControl/CardInfo/Delete?format=json",
            payload);

        if (result.Success)
            return result;

        return await SendJsonForResultAsync(
            device,
            HttpMethod.Delete,
            "/ISAPI/AccessControl/CardInfo/Delete?format=json",
            payload);
    }

    private async Task<IsapiResult> UpsertFaceAsync(
        AccessControlDevice device,
        string employeeNo,
        string imageBase64)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
            return IsapiResult.Fail("O identificador externo do usuario nao foi informado.");

        employeeNo = NormalizeEmployeeNo(employeeNo);

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(NormalizeBase64(imageBase64));
        }
        catch (FormatException)
        {
            return IsapiResult.Fail("A imagem recebida nao esta em Base64 valido.");
        }

        if (imageBytes.Length == 0)
            return IsapiResult.Fail("A imagem facial esta vazia.");

        var imageFormat = DetectSupportedFaceImageFormat(imageBytes);
        if (imageFormat is null)
        {
            return IsapiResult.Fail(
                "Formato de imagem não suportado pelo terminal Hikvision. Envie uma imagem JPEG/JPG ou PNG real; arquivos WebP, HEIC, GIF ou Base64 com conteúdo diferente da extensão são recusados.");
        }

        /*
         * IMPORTANTE: neste endpoint o JSON do part multipart NÃO deve ser
         * envelopado em { "FaceDataRecord": { ... } }.
         * O próprio campo multipart já se chama FaceDataRecord e o firmware
         * espera faceLibType, FDID e FPID diretamente na raiz do JSON.
         */
        var metadata = JsonSerializer.Serialize(new
        {
            faceLibType = "blackFD",
            FDID = DefaultFaceLibrary,
            FPID = employeeNo
        }, JsonOptions);

        // MinMoe normalmente cadastra a face com POST. Nao use PUT como
        // fallback generico, pois varios firmwares retornam methodNotAllowed.
        var postResult = await SendFaceMultipartAsync(
            device,
            HttpMethod.Post,
            "/ISAPI/Intelligent/FDLib/FaceDataRecord?format=json",
            metadata,
            imageBytes);

        if (postResult.Success)
            return postResult;

        // Se a face ja existir, remove o registro anterior e cadastra de novo.
        if (IsDuplicateFace(postResult))
        {
            var deleteResult = await DeleteFaceRecordOnlyAsync(device, employeeNo);
            if (!deleteResult.Success)
            {
                return IsapiResult.Fail(
                    $"A fotografia ja existe, mas o terminal recusou a substituicao: {deleteResult.ErrorMessage}",
                    deleteResult.Body,
                    deleteResult.StatusCode);
            }

            return await SendFaceMultipartAsync(
                device,
                HttpMethod.Post,
                "/ISAPI/Intelligent/FDLib/FaceDataRecord?format=json",
                metadata,
                imageBytes);
        }

        return postResult;
    }

    private async Task<IsapiResult> SendFaceMultipartAsync(
        AccessControlDevice device,
        HttpMethod method,
        string path,
        string metadata,
        byte[] imageBytes)
    {
        // Boundary explícito e sem aspas: alguns firmwares MinMoe são
        // extremamente rígidos ao interpretar multipart/form-data.
        var boundary = $"----------------hikvision-{Guid.NewGuid():N}";
        using var multipart = new MultipartFormDataContent(boundary);

        // Garante que o boundary enviado no cabeçalho não fique entre aspas.
        multipart.Headers.ContentType!.Parameters.Clear();
        multipart.Headers.ContentType.Parameters.Add(
            new NameValueHeaderValue("boundary", boundary));

        using var metadataContent = new StringContent(metadata, new UTF8Encoding(false));
        metadataContent.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = null
        };
        metadataContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"FaceDataRecord\""
        };

        using var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType =
            new MediaTypeHeaderValue(DetectImageContentType(imageBytes));
        imageContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"FaceImage\"",
            FileName = $"\"{DetectImageFileName(imageBytes)}\""
        };

        multipart.Add(metadataContent);
        multipart.Add(imageContent);

        return await SendContentForResultAsync(device, method, path, multipart);
    }

    private async Task<IsapiResult> DeleteFaceAsync(
        AccessControlDevice device,
        string employeeNo)
    {
        if (string.IsNullOrWhiteSpace(employeeNo))
            return IsapiResult.Fail("O identificador externo do usuario nao foi informado.");

        employeeNo = NormalizeEmployeeNo(employeeNo);

        var faceResult = await DeleteFaceRecordOnlyAsync(device, employeeNo);
        if (faceResult.Success)
            return faceResult;

        // Alguns firmwares nao permitem excluir somente a fotografia.
        // Nesse caso, a exclusao do usuario remove a face e as demais
        // credenciais associadas ao employeeNo.
        if (IsOperationNotSupported(faceResult))
        {
            var deletedUser = await DeleteUserAsync(device, employeeNo);
            return deletedUser
                ? IsapiResult.Ok("Pessoa e credenciais removidas do terminal Hikvision.")
                : IsapiResult.Fail(
                    $"O firmware nao suporta excluir somente a face e recusou a exclusao da pessoa: {faceResult.ErrorMessage}",
                    faceResult.Body,
                    faceResult.StatusCode);
        }

        return faceResult;
    }

    private async Task<IsapiResult> DeleteFaceRecordOnlyAsync(
        AccessControlDevice device,
        string employeeNo)
    {
        // Formato mais comum nos terminais MinMoe.
        var arrayPayload = new
        {
            FaceDataRecordDelCond = new
            {
                faceLibType = "blackFD",
                FDID = DefaultFaceLibrary,
                FPID = new[] { employeeNo }
            }
        };

        var result = await SendJsonForResultAsync(
            device,
            HttpMethod.Put,
            "/ISAPI/Intelligent/FDLib/FaceDataRecord/Delete?format=json",
            arrayPayload);

        if (result.Success)
            return result;

        // Variacao encontrada em alguns firmwares: FPID como texto simples.
        if (IsBadJsonContent(result))
        {
            var scalarPayload = new
            {
                FaceDataRecordDelCond = new
                {
                    faceLibType = "blackFD",
                    FDID = DefaultFaceLibrary,
                    FPID = employeeNo
                }
            };

            var scalarResult = await SendJsonForResultAsync(
                device,
                HttpMethod.Put,
                "/ISAPI/Intelligent/FDLib/FaceDataRecord/Delete?format=json",
                scalarPayload);

            if (scalarResult.Success)
                return scalarResult;

            return scalarResult;
        }

        return result;
    }

    private static bool IsDuplicateFace(IsapiResult result)
    {
        var content = string.Join(" ", result.ErrorMessage, result.Body);

        return content.Contains("alreadyExist", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("recordExist", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("deviceUserAlreadyExist", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("faceAlreadyExist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOperationNotSupported(IsapiResult result)
    {
        var content = string.Join(" ", result.ErrorMessage, result.Body);

        return content.Contains("notSupport", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("methodNotAllowed", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Invalid Operation", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("1073741825", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("1073741828", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<UserSearchResult> SearchAllUsersAsync(AccessControlDevice device)
    {
        var items = new List<JsonElement>();
        var searchId = Guid.NewGuid().ToString("N");
        var position = 0;

        while (true)
        {
            var payload = new
            {
                UserInfoSearchCond = new
                {
                    searchID = searchId,
                    searchResultPosition = position,
                    maxResults = SearchPageSize
                }
            };

            var result = await SendJsonForResultAsync(
                device,
                HttpMethod.Post,
                "/ISAPI/AccessControl/UserInfo/Search?format=json",
                payload);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Body))
                return UserSearchResult.Fail(result.ErrorMessage ?? "O terminal recusou a pesquisa de usuarios.");

            if (!LooksLikeJson(result.Body))
                return UserSearchResult.Fail("O terminal respondeu a pesquisa de usuarios em um formato ISAPI nao suportado.");

            using var document = JsonDocument.Parse(result.Body);
            var root = document.RootElement;
            var search = TryGetPropertyIgnoreCase(root, "UserInfoSearch") ?? root;
            var users = TryGetPropertyIgnoreCase(search, "UserInfo");

            var count = 0;
            if (users.HasValue && users.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var user in users.Value.EnumerateArray())
                {
                    items.Add(user.Clone());
                    count++;
                }
            }

            position += count;
            var totalMatches = JsonInt(search, "totalMatches");
            var status = JsonText(search, "responseStatusStrg");

            if (count == 0 ||
                status.Equals("NO MATCH", StringComparison.OrdinalIgnoreCase) ||
                (totalMatches > 0 && position >= totalMatches) ||
                (totalMatches <= 0 && count < SearchPageSize))
            {
                break;
            }
        }

        return UserSearchResult.Ok(items);
    }

    private async Task<DeviceCapabilities> ReadCapabilitiesAsync(AccessControlDevice device)
    {
        var users = await EndpointExistsAsync(device, "/ISAPI/AccessControl/UserInfo/capabilities?format=json");
        var card = await EndpointExistsAsync(device, "/ISAPI/AccessControl/CardInfo/capabilities?format=json");
        var events = await EndpointExistsAsync(device, "/ISAPI/AccessControl/AcsEvent/capabilities?format=json");
        var face = await EndpointExistsAsync(device, "/ISAPI/Intelligent/FDLib/capabilities?format=json");

        if (!face)
            face = await EndpointExistsAsync(device, "/ISAPI/Intelligent/FDLib/FaceDataRecord/capabilities?format=json");

        return new DeviceCapabilities(users, face, card, events);
    }

    private async Task<bool> EndpointExistsAsync(AccessControlDevice device, string path)
    {
        try
        {
            using var response = await SendAsync(device, HttpMethod.Get, path, timeout: TimeSpan.FromSeconds(8));
            return response.IsSuccessStatusCode ||
                   response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<IsapiResult> GetDeviceInfoAsync(AccessControlDevice device)
    {
        var result = await SendTextRequestForResultAsync(
            device,
            HttpMethod.Get,
            "/ISAPI/System/deviceInfo?format=json");

        if (result.Success)
            return result;

        return await SendTextRequestForResultAsync(
            device,
            HttpMethod.Get,
            "/ISAPI/System/deviceInfo");
    }

    private async Task<IsapiResult> SendJsonForResultAsync(
        AccessControlDevice device,
        HttpMethod method,
        string path,
        object payload)
    {
        var json = payload is JsonNode node
            ? node.ToJsonString(JsonOptions)
            : JsonSerializer.Serialize(payload, JsonOptions);

        using var content = CreateJsonContent(json);
        return await SendContentForResultAsync(device, method, path, content);
    }

    internal static HttpContent CreateJsonContent(string json)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private async Task<IsapiResult> SendTextForResultAsync(
        AccessControlDevice device,
        HttpMethod method,
        string path,
        string body,
        string mediaType)
    {
        using var content = new StringContent(body, Encoding.UTF8, mediaType);
        return await SendContentForResultAsync(device, method, path, content);
    }

    private async Task<IsapiResult> SendTextRequestForResultAsync(
        AccessControlDevice device,
        HttpMethod method,
        string path)
    {
        using var response = await SendAsync(device, method, path);
        var body = await response.Content.ReadAsStringAsync();
        return response.IsSuccessStatusCode
            ? IsapiResult.Ok(body)
            : IsapiResult.Fail(BuildError(response.StatusCode, body), body, response.StatusCode);
    }

    private async Task<IsapiResult> SendContentForResultAsync(
        AccessControlDevice device,
        HttpMethod method,
        string path,
        HttpContent content)
    {
        using var response = await SendAsync(device, method, path, content);
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode && !IsIsapiFailure(body))
            return IsapiResult.Ok(body, response.StatusCode);

        return IsapiResult.Fail(BuildError(response.StatusCode, body), body, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(
        AccessControlDevice device,
        HttpMethod method,
        string path,
        HttpContent? content = null,
        TimeSpan? timeout = null)
    {
        var url = $"{BaseAddress(device)}{path}";
        var handler = CreateDigestHandler(url, device);
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds)
        };

        var request = new HttpRequestMessage(method, url)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));

        try
        {
            var response = await client.SendAsync(request);
            return new OwnedHttpResponseMessage(response, client, request);
        }
        catch
        {
            request.Dispose();
            client.Dispose();
            throw;
        }
    }

    private static HttpClientHandler CreateDigestHandler(string url, AccessControlDevice device)
    {
        var credentials = new CredentialCache();
        credentials.Add(new Uri(url), "Digest", new NetworkCredential(device.Username, device.Password));

        return new HttpClientHandler
        {
            Credentials = credentials,
            PreAuthenticate = true,
            UseDefaultCredentials = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
    }

    private static bool IsIsapiFailure(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        if (LooksLikeJson(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var status = TryGetPropertyIgnoreCase(root, "ResponseStatus") ?? root;
                var statusCode = JsonInt(status, "statusCode");
                var statusString = JsonText(status, "statusString");

                return statusCode > 1 ||
                       statusString.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
                       statusString.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                       statusString.Contains("Error", StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return true;
            }
        }

        return body.Contains("<statusCode>2</statusCode>", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("<statusString>Invalid", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("<statusString>Failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildError(HttpStatusCode statusCode, string? body)
    {
        var subStatus = FirstNotEmpty(
            ReadJsonString(body, "ResponseStatus", "subStatusCode"),
            ReadJsonString(body, "subStatusCode"),
            ReadXmlValue(body, "subStatusCode"));
        var statusString = FirstNotEmpty(
            ReadJsonString(body, "ResponseStatus", "statusString"),
            ReadJsonString(body, "statusString"),
            ReadXmlValue(body, "statusString"));
        var errorCode = FirstNotEmpty(
            ReadJsonString(body, "ResponseStatus", "errorCode"),
            ReadJsonString(body, "errorCode"),
            ReadXmlValue(body, "errorCode"));

        var details = string.Join(" | ", new[] { statusString, subStatus, errorCode }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(details)
            ? $"HTTP {(int)statusCode} ({statusCode})."
            : $"HTTP {(int)statusCode} ({statusCode}): {details}.";
    }

    private static bool DetermineAccessGranted(JsonElement item)
    {
        var currentVerifyMode = JsonText(item, "currentVerifyMode");
        var attendanceStatus = JsonText(item, "attendanceStatus");
        var minor = JsonText(item, "minor");
        var status = JsonText(item, "status");

        if (status.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("success", StringComparison.OrdinalIgnoreCase))
            return true;

        if (currentVerifyMode.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            attendanceStatus.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return false;

        // Eventos MinMoe de autenticacao normalmente trazem pessoa/cartao/modo quando aceitos.
        return !string.IsNullOrWhiteSpace(JsonText(item, "employeeNoString")) ||
               !string.IsNullOrWhiteSpace(JsonText(item, "employeeNo")) ||
               !string.IsNullOrWhiteSpace(JsonText(item, "cardNo")) ||
               minor is "75" or "76";
    }

    private static bool ReadUserEnabled(JsonElement user)
    {
        var valid = TryGetPropertyIgnoreCase(user, "Valid");
        if (valid is null || valid.Value.ValueKind != JsonValueKind.Object)
            return true;

        var enable = TryGetPropertyIgnoreCase(valid.Value, "enable");
        if (enable is null)
            return true;

        return enable.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when enable.Value.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(enable.Value.GetString(), out var boolean) => boolean,
            JsonValueKind.String when long.TryParse(enable.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number != 0,
            _ => true
        };
    }

    private static DateTime? ReadNestedDateTime(JsonElement value, string objectName, string propertyName)
    {
        var child = TryGetPropertyIgnoreCase(value, objectName);
        if (child is null)
            return null;

        return ParseIsapiDate(JsonText(child.Value, propertyName));
    }

    private static JsonElement? TryGetPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static string JsonText(JsonElement value, string name)
    {
        var property = TryGetPropertyIgnoreCase(value, name);
        return property?.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString() ?? string.Empty,
            JsonValueKind.Number => property.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static int JsonInt(JsonElement value, string name)
    {
        var property = TryGetPropertyIgnoreCase(value, name);
        if (property is null)
            return 0;

        return property.Value.ValueKind switch
        {
            JsonValueKind.Number when property.Value.TryGetInt32(out var number) => number,
            JsonValueKind.Number when property.Value.TryGetInt64(out var longNumber) &&
                                      longNumber is >= int.MinValue and <= int.MaxValue => (int)longNumber,
            JsonValueKind.String when int.TryParse(property.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0
        };
    }

    internal static string ReadJsonString(string? json, params string[] path)
    {
        if (!LooksLikeJson(json))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(json!);
            var current = document.RootElement;
            foreach (var segment in path)
            {
                var next = TryGetPropertyIgnoreCase(current, segment);
                if (next is null)
                    return string.Empty;
                current = next.Value;
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString() ?? string.Empty
                : current.ToString();
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    internal static string ReadXmlValue(string? xml, string localName)
    {
        if (!LooksLikeXml(xml))
            return string.Empty;

        try
        {
            var document = XDocument.Parse(xml!);
            return document.Descendants()
                .FirstOrDefault(x => x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                ?.Value ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var first = value.AsSpan().TrimStart()[0];
        return first is '{' or '[';
    }

    private static bool LooksLikeXml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.AsSpan().TrimStart()[0] == '<';
    }

    private static DateTime EnsureValidDate(DateTime value, DateTime fallback)
    {
        if (value == default || value.Year < 2000)
            return fallback;
        return value;
    }

    private static string FormatIsapiDate(DateTime value)
    {
        var offset = value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(value),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local))
        };
        return offset.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static string FormatIsapiDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static DateTime? ParseIsapiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto))
            return dto.UtcDateTime;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
            return dt;

        return null;
    }

    private static string NormalizeEmployeeNo(string value)
    {
        value = value.Trim();
        return value.Length <= 32 ? value : value[..32];
    }

    private static string NormalizeBase64(string value)
    {
        var comma = value.IndexOf(',');
        return comma >= 0 ? value[(comma + 1)..].Trim() : value.Trim();
    }

    private static string? DetectSupportedFaceImageFormat(byte[] bytes)
    {
        // JPEG: FF D8 FF
        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return "jpeg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47 &&
            bytes[4] == 0x0D &&
            bytes[5] == 0x0A &&
            bytes[6] == 0x1A &&
            bytes[7] == 0x0A)
        {
            return "png";
        }

        return null;
    }

    private static string DetectImageContentType(byte[] bytes) =>
        DetectSupportedFaceImageFormat(bytes) switch
        {
            "png" => "image/png",
            "jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };

    private static string DetectImageFileName(byte[] bytes) =>
        DetectSupportedFaceImageFormat(bytes) == "png" ? "face.png" : "face.jpg";

    private static string TruncateUtf8(string? value, int maxBytes)
    {
        value = value?.Trim() ?? string.Empty;
        if (maxBytes <= 0 || value.Length == 0)
            return string.Empty;

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;

        var builder = new StringBuilder(value.Length);
        var usedBytes = 0;

        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (usedBytes + runeBytes > maxBytes)
                break;

            builder.Append(rune.ToString());
            usedBytes += runeBytes;
        }

        return builder.ToString();
    }

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string BaseAddress(AccessControlDevice device)
    {
        var port = device.Port is <= 0 or 80 ? string.Empty : $":{device.Port}";
        return $"http://{device.IPAddress}{port}";
    }

    private sealed record DeviceCapabilities(bool Users, bool Face, bool Card, bool Events);

    private sealed record UserSearchResult(bool Success, IReadOnlyList<JsonElement> Items, string? ErrorMessage)
    {
        public static UserSearchResult Ok(IReadOnlyList<JsonElement> items) => new(true, items, null);
        public static UserSearchResult Fail(string error) => new(false, [], error);
    }

    private sealed record IsapiResult(
        bool Success,
        string? Body,
        string? ErrorMessage,
        HttpStatusCode? StatusCode)
    {
        public static IsapiResult Ok(string? body, HttpStatusCode? statusCode = null) =>
            new(true, body, null, statusCode);

        public static IsapiResult Fail(
            string error,
            string? body = null,
            HttpStatusCode? statusCode = null) =>
            new(false, body, error, statusCode);
    }

    /// <summary>
    /// Mantem HttpClient e HttpRequestMessage vivos ate o dispose do response.
    /// </summary>
    private sealed class OwnedHttpResponseMessage : HttpResponseMessage
    {
        private readonly HttpResponseMessage _inner;
        private readonly HttpClient _client;
        private readonly HttpRequestMessage _request;

        public OwnedHttpResponseMessage(
            HttpResponseMessage inner,
            HttpClient client,
            HttpRequestMessage request)
        {
            _inner = inner;
            _client = client;
            _request = request;

            StatusCode = inner.StatusCode;
            ReasonPhrase = inner.ReasonPhrase;
            Version = inner.Version;
            Content = inner.Content;
            RequestMessage = inner.RequestMessage;

            foreach (var header in inner.Headers)
                Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _request.Dispose();
                _client.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
