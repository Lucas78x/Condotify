using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Extensions;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using System.Text;
using System.Text.Json;

public class ControlIdAccessControlDriver : IAccessControlDriver
{
    private readonly IHttpClientFactory _clientFactory;

    public bool Supports(DeviceTypeEnum type) => type.IsInControlId();

    public ControlIdAccessControlDriver(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    // TODO: Externalizar
    public static class ControlIdObjects
    {
        public const string Users = "users";
        public const string ChangeLogs = "change_logs";
        public const string Templates = "templates";
        public const string FaceTemplates = "face_templates";
        public const string Cards = "cards";
        public const string QrCodes = "qrcodes";
        public const string UhfTags = "uhf_tags";
        public const string Pins = "pins";
        public const string AlarmZones = "alarm_zones";
        public const string UserRoles = "user_roles";
        public const string Groups = "groups";
        public const string UserGroups = "user_groups";
        public const string ScheduledUnlocks = "scheduled_unlocks";
        public const string Actions = "actions";
        public const string Areas = "areas";
        public const string Portals = "portals";
        public const string PortalActions = "portal_actions";
        public const string AccessRules = "access_rules";
        public const string PortalAccessRules = "portal_access_rules";
        public const string GroupAccessRules = "group_access_rules";
        public const string ScheduledUnlockAccessRules = "scheduled_unlock_access_rules";
        public const string TimeZones = "time_zones";
        public const string TimeSpans = "time_spans";
        public const string ContingencyCards = "contingency_cards";
        public const string ContingencyCardAccessRules = "contingency_card_access_rules";
        public const string Holidays = "holidays";
        public const string AlarmZoneTimeZones = "alarm_zone_time_zones";
        public const string AccessRuleTimeZones = "access_rule_time_zones";
        public const string AccessLogs = "access_logs";
        public const string AccessLogAccessRules = "access_log_access_rules";
        public const string AlarmLogs = "alarm_logs";
        public const string Devices = "devices";
        public const string UserAccessRules = "user_access_rules";
        public const string AreaAccessRules = "area_access_rules";
        public const string CatraInfos = "catra_infos";
        public const string LogTypes = "log_types";
        public const string SecBoxs = "sec_boxs";
        public const string Contacts = "contacts";
        public const string TimedAlarms = "timed_alarms";
        public const string AccessEvents = "access_events";
        public const string CustomThresholds = "custom_thresholds";
        public const string NetworkInterlockingRules = "network_interlocking_rules";
    }

    private async Task<string?> LoginAsync(string ip, string username, string password)
    {
        try
        {
            var http = _clientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);

            var loginUrl = $"http://{ip}/login.fcgi?device_id=0";
            var payload = new
            {
                login = username,
                password,
                device_id = 0
            };

            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            using var response = await http.PostAsync(loginUrl, content);

            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body) || body.TrimStart().StartsWith('<'))
                return null;

            using var json = JsonDocument.Parse(body);

            if (!json.RootElement.TryGetProperty("session", out var sessionProp))
                return null;

            return sessionProp.GetString();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device)
    {
        var address = Address(device.IPAddress, device.Port);
        string? session = await LoginAsync(address, device.Username, device.Password);

        if (string.IsNullOrWhiteSpace(session))
            return false;

        var url = $"http://{address}/get_vpn_information.fcgi?session={session}";

        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var response = await http.GetAsync(url);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> OpenDoorAsync(AccessControlDevice device, int channel)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (string.IsNullOrWhiteSpace(session))
            return false;

        var url = $"http://{address}/execute_actions.fcgi?session={Uri.EscapeDataString(session)}";
        var payload = new
        {
            actions = new[] { new { action = "door", parameters = $"door={channel}" } }
        };

        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(15);
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
            return false;

        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body) || !body.Contains("denied", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DeviceInspectionResult> InspectAsync(AccessControlDevice device)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (string.IsNullOrWhiteSpace(session))
            return DeviceInspectionResult.Unavailable("Falha de autenticacao no terminal Control iD.");

        var system = await PostCommandForJsonAsync(address, session, "system_information.fcgi", new { });
        var portalsJson = await LoadObjectsAsync(address, session, ControlIdObjects.Portals);
        stopwatch.Stop();

        var portals = new List<DevicePortalCapability>();
        if (portalsJson is { } portalArray && portalArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in portalArray.EnumerateArray())
            {
                var number = (int)(GetInt64(item, "id") ?? 0);
                if (number <= 0) continue;
                portals.Add(new DevicePortalCapability(number, GetString(item, "name") ?? $"Portal {number}", AccessRouteDirectionEnum.Entry));
            }
        }

        if (portals.Count == 0)
            portals.Add(new DevicePortalCapability(1, "Portal principal", AccessRouteDirectionEnum.Entry, false));

        var firmware = system is { } systemValue
            ? GetString(systemValue, "version") ?? GetString(systemValue, "firmware")
            : null;
        var serialNumber = system is { } serialSystem ? GetString(serialSystem, "serial") : null;
        string? macAddress = null;
        if (system is { } networkSystem &&
            networkSystem.TryGetProperty("network", out var network) &&
            network.ValueKind == JsonValueKind.Object)
            macAddress = GetString(network, "mac");
        var capacity = system is { } value ? value.ToString() : "{}";
        return new DeviceInspectionResult(
            true,
            (int)stopwatch.ElapsedMilliseconds,
            "Terminal online e inventario atualizado.",
            firmware,
            capacity,
            portals,
            serialNumber,
            macAddress);
    }

    public async Task<List<ControlIdTagsModel>> GetUhfTagsAsync(AccessControlDevice device)
    {
        var address = Address(device.IPAddress, device.Port);
        string? session = await LoginAsync(address, device.Username, device.Password);

        if (string.IsNullOrEmpty(session))
            return new List<ControlIdTagsModel>();

        var url = $"http://{address}/load_objects.fcgi?session={session}";

        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);


        var payload = new
        {
            @object = "uhf_tags"
        };

        var content = new StringContent(JsonSerializer.Serialize(payload),
                                        Encoding.UTF8,
                                        "application/json");

        var resp = await http.PostAsync(url, content);
        string body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return new List<ControlIdTagsModel>();

        return ParseUhfTags(body);
    }

    internal static List<ControlIdTagsModel> ParseUhfTags(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return [];

        try
        {
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("objects", out var objectsNode) ||
                !objectsNode.TryGetProperty("uhf_tags", out var tagsNode) ||
                tagsNode.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var tags = new List<ControlIdTagsModel>();
            foreach (var tag in tagsNode.EnumerateArray())
            {
                if (!tag.TryGetProperty("id", out var idNode) ||
                    !idNode.TryGetInt64(out var id) ||
                    !tag.TryGetProperty("user_id", out var userNode) ||
                    !userNode.TryGetInt64(out var userId) ||
                    !tag.TryGetProperty("value", out var valueNode))
                {
                    continue;
                }

                var value = valueNode.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    tags.Add(ControlIdTagsModel.Create(id, value, userId));
            }

            return tags;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<string> GetUsersAsync(AccessControlDevice device)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null) return "[]";

        var result = await LoadObjectsAsync(address, session, ControlIdObjects.Users);

        return result?.ToString() ?? "[]";
    }

    public async Task<DeviceCredentialInventoryResult> ReadCredentialInventoryAsync(AccessControlDevice device)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null) return DeviceCredentialInventoryResult.Unavailable("Nao foi possivel autenticar no terminal Control iD.");

        var usersJson = await LoadObjectsAsync(address, session, ControlIdObjects.Users);
        if (usersJson is not { ValueKind: JsonValueKind.Array })
            return DeviceCredentialInventoryResult.Unavailable("O terminal nao retornou o inventario de usuarios.");
        var users = usersJson.Value.EnumerateArray()
            .Where(x => GetInt64(x, "id").HasValue)
            .ToDictionary(x => GetInt64(x, "id")!.Value, x => GetString(x, "name") ?? string.Empty);
        var items = new List<DeviceCredentialInventoryItem>();

        foreach (var (objectName, type) in new[]
        {
            (ControlIdObjects.FaceTemplates, AccessCredentialTypeEnum.Face),
            (ControlIdObjects.Cards, AccessCredentialTypeEnum.Card),
            (ControlIdObjects.QrCodes, AccessCredentialTypeEnum.QrCode),
            (ControlIdObjects.UhfTags, AccessCredentialTypeEnum.Tag),
            (ControlIdObjects.Pins, AccessCredentialTypeEnum.Password)
        })
        {
            var values = await LoadObjectsAsync(address, session, objectName);
            if (values is not { ValueKind: JsonValueKind.Array }) continue;
            foreach (var value in values.Value.EnumerateArray())
            {
                var id = GetInt64(value, "id")?.ToString() ?? Guid.NewGuid().ToString("N");
                var userId = GetInt64(value, "user_id")?.ToString() ?? GetString(value, "user_id") ?? string.Empty;
                var identifier = GetString(value, "value") ?? GetString(value, "card_value") ?? GetString(value, "code") ?? string.Empty;
                _ = long.TryParse(userId, out var numericUserId);
                items.Add(new DeviceCredentialInventoryItem(
                    $"{objectName}:{id}", userId, id, type, identifier,
                    users.GetValueOrDefault(numericUserId, string.Empty), true, value.GetRawText()));
            }
        }

        var representedUsers = items.Select(x => x.ExternalUserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users.Where(x => !representedUsers.Contains(x.Key.ToString())))
            items.Add(new DeviceCredentialInventoryItem($"users:{user.Key}", user.Key.ToString(), string.Empty, null, string.Empty, user.Value, true));
        return new DeviceCredentialInventoryResult(true, $"{items.Count} item(ns) lido(s) da Control iD.", items);
    }

    public async Task<string> GetEventsAsync(AccessControlDevice device)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null) return "[]";

        var result = await LoadObjectsAsync(address, session, ControlIdObjects.AccessLogs);

        return result?.ToString() ?? "[]";
    }

    public async Task<bool> AddUserAsync(AccessControlDevice device, object user)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null)
            return false;

        var ids = await CreateObjectsAsync(
            address,
            session,
            ControlIdObjects.Users,
            new[] { user }
        );

        return ids.Count > 0;
    }

    private async Task<JsonElement?> LoadObjectsAsync(
        string ip,
        string session,
        string objectName,
        int? limit = null)
    {
        /*
         * Nem todos os objetos da Control iD possuem coluna "id".
         * Tabelas relacionais, como access_rule_time_zones,
         * portal_access_rules e user_access_rules, usam chave composta.
         * Por isso, ordenar sempre por id faz alguns firmwares recusarem
         * o load_objects e impede a confirmacao do vinculo.
         */
        var effectiveLimit = limit ?? 10_000;

        object[] payloads =
        [
            new
            {
                @object = objectName,
                limit = effectiveLimit,
                offset = 0,
                order = new[] { "id", "descending" }
            },
            new
            {
                @object = objectName,
                limit = effectiveLimit,
                offset = 0
            },
            new
            {
                @object = objectName
            }
        ];

        foreach (var payload in payloads)
        {
            var result = await PostCommandDetailedAsync(
                ip,
                session,
                "load_objects.fcgi",
                payload,
                allowEmpty: false);

            if (!result.Success || result.Json is null)
                continue;

            var array = ExtractObjectArray(result.Json.Value, objectName);
            if (array.HasValue)
                return array.Value;
        }

        return null;
    }

    private static JsonElement? ExtractObjectArray(
        JsonElement root,
        string objectName)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.Clone();

        if (TryGetPropertyIgnoreCase(root, objectName, out var directArray) &&
            directArray.ValueKind == JsonValueKind.Array)
        {
            return directArray.Clone();
        }

        if (TryGetPropertyIgnoreCase(root, "objects", out var objectsNode))
        {
            if (objectsNode.ValueKind == JsonValueKind.Object &&
                TryGetPropertyIgnoreCase(objectsNode, objectName, out var nestedArray) &&
                nestedArray.ValueKind == JsonValueKind.Array)
            {
                return nestedArray.Clone();
            }

            // Alguns firmwares retornam "objects" diretamente como array.
            if (objectsNode.ValueKind == JsonValueKind.Array)
                return objectsNode.Clone();
        }

        return null;
    }

    private async Task<List<long>> CreateObjectsAsync(
        string ip,
        string session,
        string objectName,
        IEnumerable<object> values)
    {
        var materializedValues = values.ToArray();
        if (materializedValues.Length == 0)
            return [];

        var result = await PostCommandDetailedAsync(
            ip,
            session,
            "create_objects.fcgi",
            new
            {
                @object = objectName,
                values = materializedValues
            },
            allowEmpty: false);

        if (!result.Success || result.Json is null)
            return [];

        if (!TryGetPropertyIgnoreCase(result.Json.Value, "ids", out var idsNode) ||
            idsNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<long>();
        foreach (var item in idsNode.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number))
                ids.Add(number);
            else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out number))
                ids.Add(number);
        }

        return ids;
    }

    public async Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
    {
        if (!long.TryParse(userId, out var numericUserId)) return false;
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (string.IsNullOrWhiteSpace(session)) return false;

        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        var url = $"http://{address}/destroy_objects.fcgi?session={Uri.EscapeDataString(session)}";
        var payload = new { @object = ControlIdObjects.Users, where = new { users = new { id = numericUserId } } };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content);
        return response.IsSuccessStatusCode;
    }

    public async Task<CredentialOperationResult> UpsertCredentialAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null)
            return CredentialOperationResult.Fail("O equipamento Control iD recusou a autenticacao.");

        var userId = request.ExternalUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            var userIds = await CreateObjectsAsync(address, session, ControlIdObjects.Users, new[]
            {
                new
                {
                    registration = request.Registration,
                    name = request.ResidentName,
                    begin_time = ToUnix(request.ValidFrom),
                    end_time = request.IsActive ? ToUnix(request.ValidTo) : ToUnix(DateTime.UtcNow.AddSeconds(-1))
                }
            });

            if (userIds.Count == 0)
                return CredentialOperationResult.Fail("Nao foi possivel criar o usuario no equipamento Control iD.");

            userId = userIds[0].ToString();
        }

        if (!long.TryParse(userId, out var numericUserId))
            return CredentialOperationResult.Fail("O identificador externo do usuario Control iD e invalido.");

        if (request.Portals is { Count: > 0 })
        {
            var policyResult = await EnsureNativeAccessPoliciesAsync(
                address,
                session,
                numericUserId,
                request.Portals);

            if (!policyResult.Success)
            {
                return new CredentialOperationResult(
                    false,
                    userId,
                    request.ExternalCredentialId,
                    $"O usuario foi salvo, mas o terminal nao confirmou as regras nativas de acesso. {policyResult.ErrorMessage}".Trim());
            }
        }

        if (request.Type == AccessCredentialTypeEnum.Face)
        {
            if (string.IsNullOrWhiteSpace(request.ImageBase64))
                return new CredentialOperationResult(false, userId, "face", "Usuario vinculado; a captura facial ainda esta pendente.");

            var photoResult = await PostCommandAsync(address, session, "user_set_image_list.fcgi", new
            {
                user_images = new[] { new { user_id = numericUserId, image = NormalizeBase64(request.ImageBase64), timestamp = 0 } }
            });
            return photoResult
                ? CredentialOperationResult.Ok(userId, "face", "Foto facial enviada ao equipamento.")
                : CredentialOperationResult.Fail("O usuario foi criado, mas a foto facial foi recusada pelo equipamento.");
        }

        var objectName = CredentialObjectName(request.Type);
        if (objectName is null)
            return CredentialOperationResult.Fail("Este tipo de credencial ainda nao e suportado pelo driver Control iD.");

        object value = request.Type == AccessCredentialTypeEnum.Card
            ? new { value = ParseCardValue(request.Identifier), user_id = numericUserId }
            : new { value = request.Identifier.Trim(), user_id = numericUserId };
        var credentialIds = await CreateObjectsAsync(address, session, objectName, new[] { value });

        return credentialIds.Count > 0
            ? CredentialOperationResult.Ok(userId, credentialIds[0].ToString(), "Credencial sincronizada com o equipamento.")
            : CredentialOperationResult.Fail("O equipamento recusou a credencial. Verifique se o identificador ja esta em uso.");
    }

    public async Task<CredentialOperationResult> SetCredentialActiveAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request,
        bool isActive)
    {
        if (!long.TryParse(request.ExternalUserId, out var userId))
            return CredentialOperationResult.Fail("A credencial ainda nao possui usuario vinculado neste equipamento.");

        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null)
            return CredentialOperationResult.Fail("O equipamento Control iD recusou a autenticacao.");

        var changed = await ModifyObjectsAsync(address, session, ControlIdObjects.Users,
            new { begin_time = ToUnix(request.ValidFrom), end_time = isActive ? ToUnix(request.ValidTo) : ToUnix(DateTime.UtcNow.AddSeconds(-1)) },
            new { users = new { id = userId } });

        return changed
            ? CredentialOperationResult.Ok(request.ExternalUserId, request.ExternalCredentialId, isActive ? "Credencial ativada." : "Credencial suspensa.")
            : CredentialOperationResult.Fail("O equipamento nao confirmou a alteracao de validade da credencial.");
    }

    public async Task<CredentialOperationResult> RemoveCredentialAsync(
        AccessControlDevice device,
        CredentialProvisionRequest request)
    {
        if (!long.TryParse(request.ExternalUserId, out var userId))
            return CredentialOperationResult.Fail("A credencial nao possui vinculo externo neste equipamento.");

        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null)
            return CredentialOperationResult.Fail("O equipamento Control iD recusou a autenticacao.");

        bool removed;
        if (request.Type == AccessCredentialTypeEnum.Face)
        {
            removed = await PostCommandAsync(address, session, "user_destroy_image.fcgi", new { user_id = userId });
        }
        else
        {
            var objectName = CredentialObjectName(request.Type);
            if (objectName is null)
                return CredentialOperationResult.Fail("Tipo de credencial nao suportado para remocao.");
            removed = await DestroyObjectsAsync(address, session, objectName,
                long.TryParse(request.ExternalCredentialId, out var credentialId)
                    ? new Dictionary<string, object> { [objectName] = new { id = credentialId } }
                    : new Dictionary<string, object> { [objectName] = new { user_id = userId } });
        }

        return removed
            ? CredentialOperationResult.Ok(request.ExternalUserId, request.ExternalCredentialId, "Credencial removida do equipamento.")
            : CredentialOperationResult.Fail("O equipamento nao confirmou a remocao da credencial.");
    }

    public async Task<CredentialOperationResult> StartFaceEnrollmentAsync(AccessControlDevice device, string externalUserId)
    {
        if (!long.TryParse(externalUserId, out var userId))
            return CredentialOperationResult.Fail("Usuario externo invalido para captura facial.");

        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null)
            return CredentialOperationResult.Fail("O equipamento Control iD recusou a autenticacao.");

        var success = await PostCommandAsync(address, session, "remote_enroll.fcgi", new
        {
            type = "face",
            save = true,
            user_id = userId,
            sync = true,
            auto = true,
            countdown = 3
        });
        return success
            ? CredentialOperationResult.Ok(externalUserId, "face", "Captura facial iniciada no equipamento.")
            : CredentialOperationResult.Fail("Nao foi possivel iniciar a captura facial.");
    }

    public async Task<CredentialOperationResult> CancelFaceEnrollmentAsync(AccessControlDevice device)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null)
            return CredentialOperationResult.Fail("O equipamento Control iD recusou a autenticacao.");

        var success = await PostCommandAsync(address, session, "cancel_remote_enroll.fcgi", new { });
        return success ? CredentialOperationResult.Ok(null, null, "Captura cancelada.") : CredentialOperationResult.Fail("Nao foi possivel cancelar a captura.");
    }

    public async Task<IReadOnlyList<DeviceAccessEvent>> GetAccessEventsAsync(AccessControlDevice device, int take)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null) return [];

        var events = await LoadObjectsAsync(address, session, ControlIdObjects.AccessLogs, Math.Clamp(take, 1, 200));
        if (events is null) return [];

        return events.Value.EnumerateArray().Select(item =>
        {
            var eventCode = GetInt64(item, "event");
            var timestamp = GetInt64(item, "time");
            var userId = GetInt64(item, "user_id");
            var credential = GetString(item, "uhf_tag") ?? GetString(item, "qrcode_value") ?? GetInt64(item, "card_value")?.ToString();
            return new DeviceAccessEvent(
                GetInt64(item, "id")?.ToString() ?? Guid.NewGuid().ToString("N"),
                AccessEventName(eventCode),
                eventCode == 7,
                timestamp is > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime : DateTime.UtcNow,
                userId?.ToString(),
                Credential: credential,
                Portal: GetInt64(item, "portal_id")?.ToString(),
                Details: item.ToString());
        }).OrderByDescending(x => x.OccurredAt).Take(take).ToList();
    }

    private async Task<bool> ModifyObjectsAsync(
        string ip,
        string session,
        string objectName,
        object values,
        object where)
    {
        var result = await PostCommandDetailedAsync(
            ip,
            session,
            "modify_objects.fcgi",
            new
            {
                @object = objectName,
                values,
                where
            },
            allowEmpty: false);

        if (!result.Success || result.Json is null)
            return false;

        // changes == 0 tambem e sucesso: o registro pode ja possuir os mesmos valores.
        return !HasApiError(result.Json.Value);
    }

    private async Task<NativePolicyResult> EnsureNativeAccessPoliciesAsync(
        string address,
        string session,
        long userId,
        IReadOnlyList<AccessPortalAssignment> assignments)
    {
        if (assignments.Count == 0)
            return NativePolicyResult.Ok();

        var portals = await LoadObjectsAsync(address, session, ControlIdObjects.Portals);
        if (portals is not { ValueKind: JsonValueKind.Array })
            return NativePolicyResult.Fail("O equipamento nao retornou a lista nativa de portais.");

        var existingPortalIds = portals.Value
            .EnumerateArray()
            .Select(x => GetInt64(x, "id"))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();

        var validAssignments = assignments
            .Where(x => x.PortalNumber > 0)
            .ToList();

        if (validAssignments.Count == 0)
            return NativePolicyResult.Fail("Nenhum portal valido foi informado para a rota.");

        var missingPortals = validAssignments
            .Select(x => (long)x.PortalNumber)
            .Distinct()
            .Where(x => !existingPortalIds.Contains(x))
            .ToArray();

        if (missingPortals.Length > 0)
        {
            return NativePolicyResult.Fail(
                $"Os portais {string.Join(", ", missingPortals)} nao existem no equipamento.");
        }

        var desiredAccessRuleIds = new HashSet<long>();

        var routes = validAssignments.GroupBy(
            x => string.IsNullOrWhiteSpace(x.RouteName) ? "Rota principal" : x.RouteName.Trim(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var route in routes)
        {
            var routeAssignments = route.ToList();
            var first = routeAssignments[0];
            var nativeName = NativePolicyName(route.Key);

            var timeZoneResult = await EnsureNamedObjectAsync(
                address,
                session,
                ControlIdObjects.TimeZones,
                nativeName,
                () => new { name = nativeName });

            if (!timeZoneResult.Success)
                return NativePolicyResult.Fail($"Falha ao criar/localizar o horario '{nativeName}': {timeZoneResult.ErrorMessage}");

            var accessRuleResult = await EnsureNamedObjectAsync(
                address,
                session,
                ControlIdObjects.AccessRules,
                nativeName,
                () => new { name = nativeName, type = 1, priority = 0 });

            if (!accessRuleResult.Success)
                return NativePolicyResult.Fail($"Falha ao criar/localizar a regra '{nativeName}': {accessRuleResult.ErrorMessage}");

            var timeZoneId = timeZoneResult.Id!.Value;
            var accessRuleId = accessRuleResult.Id!.Value;
            desiredAccessRuleIds.Add(accessRuleId);

            var timeResult = await EnsureTimeSpanAsync(
                address,
                session,
                timeZoneId,
                first);

            if (!timeResult.Success)
                return NativePolicyResult.Fail($"Falha ao confirmar o horario da rota '{route.Key}': {timeResult.ErrorMessage}");

            var timeZoneRelation = await EnsureRelationAsync(
                address,
                session,
                ControlIdObjects.AccessRuleTimeZones,
                new { access_rule_id = accessRuleId, time_zone_id = timeZoneId },
                item => GetInt64(item, "access_rule_id") == accessRuleId &&
                        GetInt64(item, "time_zone_id") == timeZoneId);

            if (!timeZoneRelation.Success)
                return NativePolicyResult.Fail($"Falha ao vincular a regra ao horario: {timeZoneRelation.ErrorMessage}");

            foreach (var portalId in routeAssignments.Select(x => (long)x.PortalNumber).Distinct())
            {
                var portalRelation = await EnsureRelationAsync(
                    address,
                    session,
                    ControlIdObjects.PortalAccessRules,
                    new { portal_id = portalId, access_rule_id = accessRuleId },
                    item => GetInt64(item, "portal_id") == portalId &&
                            GetInt64(item, "access_rule_id") == accessRuleId);

                if (!portalRelation.Success)
                {
                    return NativePolicyResult.Fail(
                        $"Falha ao vincular o portal {portalId} a regra '{route.Key}': {portalRelation.ErrorMessage}");
                }
            }

            // Vinculo direto usuario -> regra. E mais compativel do que criar grupos
            // dinamicos e e oficialmente suportado pela API Control iD.
            var userRelation = await EnsureRelationAsync(
                address,
                session,
                ControlIdObjects.UserAccessRules,
                new { user_id = userId, access_rule_id = accessRuleId },
                item => GetInt64(item, "user_id") == userId &&
                        GetInt64(item, "access_rule_id") == accessRuleId);

            if (!userRelation.Success)
                return NativePolicyResult.Fail($"Falha ao vincular o usuario a regra: {userRelation.ErrorMessage}");
        }

        // Remove apenas vinculos diretos antigos gerenciados pelo Condotify.
        var rules = await LoadObjectsAsync(address, session, ControlIdObjects.AccessRules);
        var userRules = await LoadObjectsAsync(address, session, ControlIdObjects.UserAccessRules);

        if (rules is { ValueKind: JsonValueKind.Array } &&
            userRules is { ValueKind: JsonValueKind.Array })
        {
            var managedRuleIds = rules.Value
                .EnumerateArray()
                .Where(x => (GetString(x, "name") ?? string.Empty)
                    .StartsWith("Condotify - ", StringComparison.OrdinalIgnoreCase))
                .Select(x => GetInt64(x, "id"))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToHashSet();

            var staleRuleIds = userRules.Value
                .EnumerateArray()
                .Where(x => GetInt64(x, "user_id") == userId)
                .Select(x => GetInt64(x, "access_rule_id"))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Where(x => managedRuleIds.Contains(x) && !desiredAccessRuleIds.Contains(x))
                .Distinct()
                .ToArray();

            foreach (var staleRuleId in staleRuleIds)
            {
                await DestroyObjectsAsync(
                    address,
                    session,
                    ControlIdObjects.UserAccessRules,
                    new
                    {
                        user_access_rules = new
                        {
                            user_id = userId,
                            access_rule_id = staleRuleId
                        }
                    });
            }
        }

        return NativePolicyResult.Ok();
    }

    private async Task<NamedObjectResult> EnsureNamedObjectAsync(
        string address,
        string session,
        string objectName,
        string name,
        Func<object> createValue)
    {
        var objects = await LoadObjectsAsync(address, session, objectName);
        if (objects is { ValueKind: JsonValueKind.Array })
        {
            foreach (var item in objects.Value.EnumerateArray())
            {
                if (!string.Equals(GetString(item, "name"), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = GetInt64(item, "id");
                if (id is > 0)
                    return NamedObjectResult.Ok(id.Value);
            }
        }

        var ids = await CreateObjectsAsync(address, session, objectName, [createValue()]);
        if (ids.Count > 0 && ids[0] > 0)
            return NamedObjectResult.Ok(ids[0]);

        // Pode ter ocorrido uma corrida ou o equipamento pode ter retornado conflito.
        // Recarregamos para confirmar se o objeto foi criado mesmo assim.
        objects = await LoadObjectsAsync(address, session, objectName);
        if (objects is { ValueKind: JsonValueKind.Array })
        {
            foreach (var item in objects.Value.EnumerateArray())
            {
                if (!string.Equals(GetString(item, "name"), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var existingId = GetInt64(item, "id");
                if (existingId is > 0)
                    return NamedObjectResult.Ok(existingId.Value);
            }
        }

        return NamedObjectResult.Fail($"O equipamento recusou o objeto '{objectName}'.");
    }

    private async Task<NativePolicyResult> EnsureTimeSpanAsync(
        string address,
        string session,
        long timeZoneId,
        AccessPortalAssignment assignment)
    {
        var start = NormalizeTimeSeconds(assignment.StartTime, isEnd: false);
        var end = NormalizeTimeSeconds(assignment.EndTime, isEnd: true);

        // 00:00 ate 00:00 e tratado como dia inteiro.
        if (start == 0 && end == 0)
            end = 86_399;

        if (end <= start)
        {
            return NativePolicyResult.Fail(
                $"Intervalo invalido: inicio {assignment.StartTime} e fim {assignment.EndTime}.");
        }

        var daysMask = assignment.DaysOfWeekMask == 0
            ? 127
            : assignment.DaysOfWeekMask;

        var values = new
        {
            time_zone_id = timeZoneId,
            start,
            end,
            sun = DayFlag(daysMask, 1),
            mon = DayFlag(daysMask, 2),
            tue = DayFlag(daysMask, 4),
            wed = DayFlag(daysMask, 8),
            thu = DayFlag(daysMask, 16),
            fri = DayFlag(daysMask, 32),
            sat = DayFlag(daysMask, 64),
            hol1 = 0,
            hol2 = 0,
            hol3 = 0
        };

        var spans = await LoadObjectsAsync(address, session, ControlIdObjects.TimeSpans);
        if (spans is { ValueKind: JsonValueKind.Array })
        {
            foreach (var existing in spans.Value.EnumerateArray())
            {
                if (GetInt64(existing, "time_zone_id") != timeZoneId)
                    continue;

                var id = GetInt64(existing, "id");
                if (id is not > 0)
                    continue;

                if (TimeSpanMatches(existing, timeZoneId, start, end, daysMask))
                    return NativePolicyResult.Ok();

                var modified = await ModifyObjectsAsync(
                    address,
                    session,
                    ControlIdObjects.TimeSpans,
                    values,
                    new { time_spans = new { id = id.Value } });

                if (!modified)
                    return NativePolicyResult.Fail("O equipamento recusou a atualizacao do intervalo de horario.");

                return NativePolicyResult.Ok();
            }
        }

        var ids = await CreateObjectsAsync(
            address,
            session,
            ControlIdObjects.TimeSpans,
            [values]);

        return ids.Count > 0
            ? NativePolicyResult.Ok()
            : NativePolicyResult.Fail("O equipamento recusou a criacao do intervalo de horario.");
    }

    private async Task<NativePolicyResult> EnsureRelationAsync(
        string address,
        string session,
        string objectName,
        object value,
        Func<JsonElement, bool> matches)
    {
        // Primeiro confirma se o vinculo ja existe.
        var objects = await LoadObjectsAsync(address, session, objectName);
        if (objects is { ValueKind: JsonValueKind.Array } &&
            objects.Value.EnumerateArray().Any(matches))
        {
            return NativePolicyResult.Ok();
        }

        /*
         * Tabelas relacionais da Control iD normalmente nao retornam "ids".
         * Elas podem responder apenas { "changes": 1 } ou ate um objeto vazio.
         * Portanto, nao podemos usar CreateObjectsAsync, que exige o array ids.
         */
        var createResult = await PostCommandDetailedAsync(
            address,
            session,
            "create_objects.fcgi",
            new
            {
                @object = objectName,
                values = new[] { value }
            },
            allowEmpty: true);

        if (createResult.Success &&
            createResult.Json is { } createdJson &&
            !HasApiError(createdJson))
        {
            // A resposta HTTP/API valida ja confirma a criacao. A releitura
            // abaixo serve apenas como verificacao adicional quando suportada.
            objects = await LoadObjectsAsync(address, session, objectName);

            if (objects is not { ValueKind: JsonValueKind.Array } ||
                objects.Value.EnumerateArray().Any(matches))
            {
                return NativePolicyResult.Ok();
            }

            /*
             * Alguns firmwares aceitam o create_objects, mas nao permitem
             * listar a tabela relacional. Nessa situacao, a confirmacao da
             * propria API deve ser considerada sucesso.
             */
            return NativePolicyResult.Ok();
        }

        // Em caso de conflito/duplicidade, uma ultima releitura pode confirmar
        // que o vinculo ja existia, mesmo que o create tenha sido recusado.
        objects = await LoadObjectsAsync(address, session, objectName);
        if (objects is { ValueKind: JsonValueKind.Array } &&
            objects.Value.EnumerateArray().Any(matches))
        {
            return NativePolicyResult.Ok();
        }

        return NativePolicyResult.Fail(
            createResult.ErrorMessage ??
            $"O equipamento recusou o vinculo '{objectName}'.");
    }

    private static bool TimeSpanMatches(
        JsonElement item,
        long timeZoneId,
        int start,
        int end,
        int daysMask)
    {
        return GetInt64(item, "time_zone_id") == timeZoneId &&
               GetInt64(item, "start") == start &&
               GetInt64(item, "end") == end &&
               GetInt64(item, "sun") == DayFlag(daysMask, 1) &&
               GetInt64(item, "mon") == DayFlag(daysMask, 2) &&
               GetInt64(item, "tue") == DayFlag(daysMask, 4) &&
               GetInt64(item, "wed") == DayFlag(daysMask, 8) &&
               GetInt64(item, "thu") == DayFlag(daysMask, 16) &&
               GetInt64(item, "fri") == DayFlag(daysMask, 32) &&
               GetInt64(item, "sat") == DayFlag(daysMask, 64);
    }

    private static int NormalizeTimeSeconds(TimeSpan value, bool isEnd)
    {
        if (value < TimeSpan.Zero)
            return 0;

        if (value.TotalSeconds >= 86_400)
            return isEnd ? 86_399 : 0;

        return Math.Clamp((int)value.TotalSeconds, 0, 86_399);
    }

    private static string NativePolicyName(string routeName)
    {
        var value = $"Condotify - {routeName.Trim()}";
        return value.Length <= 80 ? value : value[..80];
    }

    private static int DayFlag(int mask, int bit) => (mask & bit) != 0 ? 1 : 0;

    private async Task<bool> DestroyObjectsAsync(
        string ip,
        string session,
        string objectName,
        object where)
    {
        var result = await PostCommandDetailedAsync(
            ip,
            session,
            "destroy_objects.fcgi",
            new { @object = objectName, where },
            allowEmpty: false);

        if (!result.Success || result.Json is null)
            return false;

        return !HasApiError(result.Json.Value);
    }

    private async Task<bool> PostCommandWithChangeCountAsync(
        string ip,
        string session,
        string endpoint,
        object payload)
    {
        var result = await PostCommandDetailedAsync(
            ip,
            session,
            endpoint,
            payload,
            allowEmpty: false);

        return result.Success &&
               result.Json is not null &&
               !HasApiError(result.Json.Value);
    }

    private async Task<bool> PostCommandAsync(
        string ip,
        string session,
        string endpoint,
        object payload)
    {
        var result = await PostCommandDetailedAsync(
            ip,
            session,
            endpoint,
            payload,
            allowEmpty: true);

        if (!result.Success || result.Json is null)
            return false;

        var root = result.Json.Value;
        if (TryGetPropertyIgnoreCase(root, "success", out var success))
        {
            return success.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when success.TryGetInt64(out var number) => number != 0,
                JsonValueKind.String when bool.TryParse(success.GetString(), out var flag) => flag,
                _ => !HasApiError(root)
            };
        }

        return !HasApiError(root);
    }

    private async Task<JsonElement?> PostCommandForJsonAsync(
        string ip,
        string session,
        string endpoint,
        object payload,
        bool allowEmpty = false)
    {
        var result = await PostCommandDetailedAsync(
            ip,
            session,
            endpoint,
            payload,
            allowEmpty);

        return result.Success ? result.Json : null;
    }

    private async Task<ApiCommandResult> PostCommandDetailedAsync(
        string ip,
        string session,
        string endpoint,
        object payload,
        bool allowEmpty)
    {
        try
        {
            var http = _clientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.ExpectContinue = false;

            var url = $"http://{ip}/{endpoint}?session={Uri.EscapeDataString(session)}";
            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                new UTF8Encoding(false),
                "application/json");

            using var response = await http.PostAsync(url, content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return ApiCommandResult.Fail(
                    $"HTTP {(int)response.StatusCode} ({response.StatusCode}): {TrimErrorBody(body)}");
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return allowEmpty
                    ? ApiCommandResult.Ok(JsonSerializer.SerializeToElement(new { success = true }))
                    : ApiCommandResult.Fail("O equipamento respondeu sem conteudo.");
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement.Clone();

                if (HasApiError(root))
                    return ApiCommandResult.Fail(ReadApiError(root), root);

                return ApiCommandResult.Ok(root);
            }
            catch (JsonException exception)
            {
                return ApiCommandResult.Fail(
                    $"Resposta JSON invalida: {exception.Message}. Corpo: {TrimErrorBody(body)}");
            }
        }
        catch (TaskCanceledException)
        {
            return ApiCommandResult.Fail("Tempo limite excedido ao comunicar com o equipamento.");
        }
        catch (HttpRequestException exception)
        {
            return ApiCommandResult.Fail($"Falha HTTP: {exception.Message}");
        }
        catch (Exception exception)
        {
            return ApiCommandResult.Fail($"Falha inesperada: {exception.Message}");
        }
    }

    private static bool HasApiError(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "error", out var error))
            return false;

        return error.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.False => false,
            JsonValueKind.Number when error.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(error.GetString()),
            _ => true
        };
    }

    private static string ReadApiError(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "error", out var error))
            return "Erro nao informado pelo equipamento.";

        if (error.ValueKind == JsonValueKind.Object)
        {
            var message = GetString(error, "message") ??
                          GetString(error, "description") ??
                          GetString(error, "msg");
            var code = GetString(error, "code");

            return string.Join(" | ", new[] { message, code }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        return error.ToString();
    }

    private static string TrimErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "sem detalhes";

        var value = body.Trim();
        return value.Length <= 500 ? value : value[..500];
    }

    private static string? CredentialObjectName(AccessCredentialTypeEnum type) => type switch
    {
        AccessCredentialTypeEnum.Card => ControlIdObjects.Cards,
        AccessCredentialTypeEnum.QrCode => ControlIdObjects.QrCodes,
        AccessCredentialTypeEnum.Tag or AccessCredentialTypeEnum.VehicleTag => ControlIdObjects.UhfTags,
        AccessCredentialTypeEnum.Password => ControlIdObjects.Pins,
        _ => null
    };

    private static long ParseCardValue(string identifier)
    {
        var parts = identifier.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && long.TryParse(parts[0], out var site) && uint.TryParse(parts[1], out var card))
            return checked(site * 4294967296L + card);
        return long.TryParse(identifier, out var value) ? value : throw new ArgumentException("Numero de cartao invalido.");
    }

    private static string NormalizeBase64(string value) => value.Contains(',') ? value[(value.IndexOf(',') + 1)..] : value;
    private static string Address(string ip, int port) => port is <= 0 or 80 ? ip : $"{ip}:{port}";
    private static long ToUnix(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();
    private static bool TryGetPropertyIgnoreCase(
        JsonElement item,
        string name,
        out JsonElement value)
    {
        value = default;

        if (item.ValueKind != JsonValueKind.Object)
            return false;

        if (item.TryGetProperty(name, out value))
            return true;

        foreach (var property in item.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static long? GetInt64(JsonElement item, string name)
    {
        if (!TryGetPropertyIgnoreCase(item, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => null
        };
    }

    private static string? GetString(JsonElement item, string name)
    {
        if (!TryGetPropertyIgnoreCase(item, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private sealed record NativePolicyResult(bool Success, string? ErrorMessage)
    {
        public static NativePolicyResult Ok() => new(true, null);
        public static NativePolicyResult Fail(string error) => new(false, error);
    }

    private sealed record NamedObjectResult(bool Success, long? Id, string? ErrorMessage)
    {
        public static NamedObjectResult Ok(long id) => new(true, id, null);
        public static NamedObjectResult Fail(string error) => new(false, null, error);
    }

    private sealed record ApiCommandResult(bool Success, JsonElement? Json, string? ErrorMessage)
    {
        public static ApiCommandResult Ok(JsonElement json) => new(true, json, null);
        public static ApiCommandResult Fail(string error, JsonElement? json = null) => new(false, json, error);
    }

    private static string AccessEventName(long? code) => code switch
    {
        6 => "Acesso negado",
        7 => "Acesso autorizado",
        10 => "Abertura pela API",
        11 => "Abertura por botoeira",
        12 => "Abertura pela interface web",
        15 => "Acesso pela interfonia",
        _ => $"Evento {code?.ToString() ?? "desconhecido"}"
    };
}
