using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Extensions;
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

        var loginUrl = $"http://{ip}/login.fcgi";

        var payload = new
        {
            login = username,
            password = password
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
        string? session = await LoginAsync(device.IPAddress, device.Username, device.Password);

        if (string.IsNullOrWhiteSpace(session))
            return false;

        var url = $"http://{device.IPAddress}/get_vpn_information.fcgi?session={session}";

        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var response = await http.GetAsync(url);

        return response.IsSuccessStatusCode;
    }

    public async Task<List<ControlIdTagsModel>> GetUhfTagsAsync(AccessControlDevice device)
    {
        string? session = await LoginAsync(device.IPAddress, device.Username, device.Password);

        if (string.IsNullOrEmpty(session))
            return new List<ControlIdTagsModel>();

        var url = $"http://{device.IPAddress}/load_objects.fcgi?session={session}";

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
        var session = await LoginAsync(device.IPAddress, device.Username, device.Password);
        if (session is null) return "[]";

        var result = await LoadObjectsAsync(device.IPAddress, session, ControlIdObjects.Users);

        return result?.ToString() ?? "[]";
    }

    public async Task<string> GetEventsAsync(AccessControlDevice device)
    {
        var session = await LoginAsync(device.IPAddress, device.Username, device.Password);
        if (session is null) return "[]";

        var result = await LoadObjectsAsync(device.IPAddress, session, ControlIdObjects.AccessLogs);

        return result?.ToString() ?? "[]";
    }

    public async Task<bool> AddUserAsync(AccessControlDevice device, object user)
    {
        var session = await LoginAsync(device.IPAddress, device.Username, device.Password);
        if (session is null)
            return false;

        var ids = await CreateObjectsAsync(
            device.IPAddress,
            session,
            ControlIdObjects.Users,
            new[] { user }
        );

        return ids.Count > 0;
    }

    private async Task<JsonElement?> LoadObjectsAsync(string ip, string session, string objectName)
    {
        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var url = $"http://{ip}/load_objects.fcgi?session={session}";

        var payload = new { @object = objectName };

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

        if (json.RootElement.TryGetProperty("objects", out var objectsNode) &&
            objectsNode.TryGetProperty(objectName, out var arr))
        {
            return arr;
        }

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

    public Task<bool> OpenDoorAsync(AccessControlDevice device)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> TestConnectionAsync(AccessControlDevice device)
    {

        string? session = await LoginAsync(device.IPAddress, device.Username, device.Password);

        if (string.IsNullOrWhiteSpace(session))
            return false;

        var url = $"http://{device.IPAddress}/get_vpn_information.fcgi?session={session}";

        var http = _clientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var response = await http.GetAsync(url);

        return response.IsSuccessStatusCode;
    }
}
