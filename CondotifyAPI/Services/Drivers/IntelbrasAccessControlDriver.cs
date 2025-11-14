using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Extensions;
using System.Net.Http.Headers;

public class IntelbrasAccessControlDriver : IAccessControlDriver
{
    private readonly IHttpClientFactory _clientFactory;

    public bool Supports(DeviceTypeEnum type) => type.IsInIntelbras();

    public IntelbrasAccessControlDriver(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<bool> TestConnectionAsync(AccessControlDevice device)
    {
        var url = $"http://{device.IPAddress}/cgi-bin/configManager.cgi?action=getConfig&name=Network";

        var client = _clientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var byteArray = System.Text.Encoding.UTF8.GetBytes($"{device.Username}:{device.Password}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

        var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode;
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
