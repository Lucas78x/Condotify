
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

    public Task<string> GetUsersAsync(AccessControlDevice device)
        => throw new NotImplementedException();

    public Task<bool> AddUserAsync(AccessControlDevice device, object user)
        => throw new NotImplementedException();

    public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
        => throw new NotImplementedException();

    public Task<string> GetEventsAsync(AccessControlDevice device)
        => throw new NotImplementedException();
}
