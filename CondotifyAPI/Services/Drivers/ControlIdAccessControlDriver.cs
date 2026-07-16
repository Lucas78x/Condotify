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
        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var loginUrl = $"http://{ip}/login.fcgi?device_id=0";

        var payload = new
        {
            login = username,
            password = password,
            device_id = 0
        };

        var content = new StringContent(JsonSerializer.Serialize(payload),
                                        Encoding.UTF8,
                                        "application/json");

        var response = await http.PostAsync(loginUrl, content);

        if (!response.IsSuccessStatusCode)
            return null;

        string body = await response.Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);

        if (!json.RootElement.TryGetProperty("session", out var sessionProp))
            return null;

        return sessionProp.GetString();
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

        var firmware = system is { } systemValue && systemValue.TryGetProperty("firmware", out var firmwareValue)
            ? firmwareValue.ToString()
            : null;
        var capacity = system is { } value ? value.ToString() : "{}";
        return new DeviceInspectionResult(true, (int)stopwatch.ElapsedMilliseconds, "Terminal online e inventario atualizado.", firmware, capacity, portals);
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

        using var json = JsonDocument.Parse(body);

        var list = new List<ControlIdTagsModel>();

        if (!json.RootElement.TryGetProperty("objects", out var objectsNode))
            return list;

        if (!objectsNode.TryGetProperty("uhf_tags", out var tagsNode))
            return list;

        foreach (var tag in tagsNode.EnumerateArray())
        {
            list.Add(ControlIdTagsModel.Create(
                tag.GetProperty("id").GetInt64(),
                tag.GetProperty("value").GetString(),
                tag.GetProperty("user_id").GetInt64()
            ));
        }

        return list;
    }

    public async Task<string> GetUsersAsync(AccessControlDevice device)
    {
        var address = Address(device.IPAddress, device.Port);
        var session = await LoginAsync(address, device.Username, device.Password);
        if (session is null) return "[]";

        var result = await LoadObjectsAsync(address, session, ControlIdObjects.Users);

        return result?.ToString() ?? "[]";
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

    private async Task<JsonElement?> LoadObjectsAsync(string ip, string session, string objectName, int? limit = null)
    {
        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var url = $"http://{ip}/load_objects.fcgi?session={session}";

        var payload = new { @object = objectName, limit, order = new[] { "id", "descending" } };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var resp = await http.PostAsync(url, content);
        if (!resp.IsSuccessStatusCode)
            return null;

        var body = await resp.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        if (json.RootElement.TryGetProperty(objectName, out var directArray))
            return directArray.Clone();

        if (json.RootElement.TryGetProperty("objects", out var objectsNode) &&
            objectsNode.TryGetProperty(objectName, out var nestedArray))
            return nestedArray.Clone();

        return null;
    }
    private async Task<List<long>> CreateObjectsAsync(
    string ip,
    string session,
    string objectName,
    IEnumerable<object> values)
    {
        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var url = $"http://{ip}/create_objects.fcgi?session={session}";

        var payload = new
        {
            @object = objectName,
            values = values
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var resp = await http.PostAsync(url, content);
        if (!resp.IsSuccessStatusCode)
            return new List<long>();

        var body = await resp.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        if (!json.RootElement.TryGetProperty("ids", out var idsNode))
            return new List<long>();

        return idsNode.EnumerateArray().Select(e => e.GetInt64()).ToList();
    }

    public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
        => throw new NotImplementedException();

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

        if (request.Portals is { Count: > 0 } && !await EnsureNativeAccessPoliciesAsync(address, session, numericUserId, request.Portals))
            return new CredentialOperationResult(false, userId, request.ExternalCredentialId,
                "O usuario foi salvo, mas o terminal nao confirmou as regras nativas de rota, portal e horario.");

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

    private async Task<bool> ModifyObjectsAsync(string ip, string session, string objectName, object values, object where) =>
        await PostCommandWithChangeCountAsync(ip, session, "modify_objects.fcgi", new { @object = objectName, values, where });

    private async Task<bool> EnsureNativeAccessPoliciesAsync(
        string address,
        string session,
        long userId,
        IReadOnlyList<AccessPortalAssignment> assignments)
    {
        var desiredGroupIds = new HashSet<long>();
        var desiredRules = assignments.GroupBy(x => x.RouteName, StringComparer.OrdinalIgnoreCase);

        foreach (var route in desiredRules)
        {
            var first = route.First();
            var nativeName = NativePolicyName(route.Key);
            var groupId = await EnsureNamedObjectAsync(address, session, ControlIdObjects.Groups, nativeName,
                () => new { name = nativeName });
            var timeZoneId = await EnsureNamedObjectAsync(address, session, ControlIdObjects.TimeZones, nativeName,
                () => new { name = nativeName });
            var accessRuleId = await EnsureNamedObjectAsync(address, session, ControlIdObjects.AccessRules, nativeName,
                () => new { name = nativeName, type = 1, priority = 0 });
            if (groupId is null || timeZoneId is null || accessRuleId is null) return false;

            desiredGroupIds.Add(groupId.Value);
            if (!await EnsureTimeSpanAsync(address, session, timeZoneId.Value, first)) return false;
            if (!await EnsureRelationAsync(address, session, ControlIdObjects.GroupAccessRules,
                    new { group_id = groupId.Value, access_rule_id = accessRuleId.Value },
                    item => GetInt64(item, "group_id") == groupId && GetInt64(item, "access_rule_id") == accessRuleId)) return false;
            if (!await EnsureRelationAsync(address, session, ControlIdObjects.AccessRuleTimeZones,
                    new { access_rule_id = accessRuleId.Value, time_zone_id = timeZoneId.Value },
                    item => GetInt64(item, "access_rule_id") == accessRuleId && GetInt64(item, "time_zone_id") == timeZoneId)) return false;

            foreach (var portal in route.Select(x => x.PortalNumber).Distinct())
            {
                if (!await EnsureRelationAsync(address, session, ControlIdObjects.PortalAccessRules,
                        new { portal_id = portal, access_rule_id = accessRuleId.Value },
                        item => GetInt64(item, "portal_id") == portal && GetInt64(item, "access_rule_id") == accessRuleId)) return false;
            }

            if (!await EnsureRelationAsync(address, session, ControlIdObjects.UserGroups,
                    new { user_id = userId, group_id = groupId.Value },
                    item => GetInt64(item, "user_id") == userId && GetInt64(item, "group_id") == groupId)) return false;
        }

        var groups = await LoadObjectsAsync(address, session, ControlIdObjects.Groups);
        var userGroups = await LoadObjectsAsync(address, session, ControlIdObjects.UserGroups);
        if (groups is { } groupsArray && userGroups is { } linksArray)
        {
            var managedGroupIds = groupsArray.EnumerateArray()
                .Where(x => (GetString(x, "name") ?? string.Empty).StartsWith("Condotify - ", StringComparison.OrdinalIgnoreCase))
                .Select(x => GetInt64(x, "id"))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToHashSet();
            foreach (var stale in linksArray.EnumerateArray().Where(x => GetInt64(x, "user_id") == userId)
                         .Select(x => GetInt64(x, "group_id")).Where(x => x.HasValue)
                         .Select(x => x!.Value).Where(x => managedGroupIds.Contains(x) && !desiredGroupIds.Contains(x)))
            {
                await DestroyObjectsAsync(address, session, ControlIdObjects.UserGroups,
                    new { user_groups = new { user_id = userId, group_id = stale } });
            }
        }

        return true;
    }

    private async Task<long?> EnsureNamedObjectAsync(
        string address,
        string session,
        string objectName,
        string name,
        Func<object> createValue)
    {
        var objects = await LoadObjectsAsync(address, session, objectName);
        var existing = objects?.EnumerateArray().FirstOrDefault(x =>
            string.Equals(GetString(x, "name"), name, StringComparison.OrdinalIgnoreCase));
        var existingId = existing.HasValue ? GetInt64(existing.Value, "id") : null;
        if (existingId.HasValue) return existingId;
        return (await CreateObjectsAsync(address, session, objectName, [createValue()])).FirstOrDefault() is var id && id > 0 ? id : null;
    }

    private async Task<bool> EnsureTimeSpanAsync(
        string address,
        string session,
        long timeZoneId,
        AccessPortalAssignment assignment)
    {
        var values = new
        {
            time_zone_id = timeZoneId,
            start = (int)assignment.StartTime.TotalSeconds,
            end = Math.Min(86_399, (int)assignment.EndTime.TotalSeconds),
            sun = DayFlag(assignment.DaysOfWeekMask, 1), mon = DayFlag(assignment.DaysOfWeekMask, 2),
            tue = DayFlag(assignment.DaysOfWeekMask, 4), wed = DayFlag(assignment.DaysOfWeekMask, 8),
            thu = DayFlag(assignment.DaysOfWeekMask, 16), fri = DayFlag(assignment.DaysOfWeekMask, 32),
            sat = DayFlag(assignment.DaysOfWeekMask, 64), hol1 = 0, hol2 = 0, hol3 = 0
        };
        var spans = await LoadObjectsAsync(address, session, ControlIdObjects.TimeSpans);
        var existing = spans?.EnumerateArray().FirstOrDefault(x => GetInt64(x, "time_zone_id") == timeZoneId);
        var id = existing.HasValue ? GetInt64(existing.Value, "id") : null;
        return id.HasValue
            ? await ModifyObjectsAsync(address, session, ControlIdObjects.TimeSpans, values, new { time_spans = new { id } })
            : (await CreateObjectsAsync(address, session, ControlIdObjects.TimeSpans, [values])).Count > 0;
    }

    private async Task<bool> EnsureRelationAsync(
        string address,
        string session,
        string objectName,
        object value,
        Func<JsonElement, bool> matches)
    {
        var objects = await LoadObjectsAsync(address, session, objectName);
        if (objects is { } array && array.EnumerateArray().Any(matches)) return true;
        return (await CreateObjectsAsync(address, session, objectName, [value])).Count > 0;
    }

    private static string NativePolicyName(string routeName)
    {
        var value = $"Condotify - {routeName.Trim()}";
        return value.Length <= 80 ? value : value[..80];
    }

    private static int DayFlag(int mask, int bit) => (mask & bit) != 0 ? 1 : 0;

    private async Task<bool> DestroyObjectsAsync(string ip, string session, string objectName, object where) =>
        await PostCommandWithChangeCountAsync(ip, session, "destroy_objects.fcgi", new { @object = objectName, where });

    private async Task<bool> PostCommandWithChangeCountAsync(string ip, string session, string endpoint, object payload)
    {
        var response = await PostCommandForJsonAsync(ip, session, endpoint, payload);
        return response is not null && (!response.Value.TryGetProperty("changes", out var changes) || changes.GetInt32() > 0);
    }

    private async Task<bool> PostCommandAsync(string ip, string session, string endpoint, object payload)
    {
        var result = await PostCommandForJsonAsync(ip, session, endpoint, payload, allowEmpty: true);
        if (result is null) return false;
        if (result.Value.TryGetProperty("success", out var success) && success.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return success.GetBoolean();
        return !result.Value.TryGetProperty("error", out _);
    }

    private async Task<JsonElement?> PostCommandForJsonAsync(string ip, string session, string endpoint, object payload, bool allowEmpty = false)
    {
        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        var url = $"http://{ip}/{endpoint}?session={Uri.EscapeDataString(session)}";
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return allowEmpty ? JsonSerializer.SerializeToElement(new { success = true }) : null;
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
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
    private static long? GetInt64(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static string? GetString(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
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
