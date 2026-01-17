using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Extensions;
using System.Net;
using System.Net.Http.Headers;

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
        var url = $"http://{device.IPAddress}/cgi-bin/configManager.cgi?action=getConfig&name=Network";

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(device.Username, device.Password),
            PreAuthenticate = true
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

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

    public async Task<bool> OpenDoorAsync(AccessControlDevice device)
    {
        var url = $"http://{device.IPAddress}/cgi-bin/accessControl.cgi?action=openDoor&channel=1";

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(device.Username, device.Password),
            PreAuthenticate = true
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        try
        {
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsStringAsync();

            return content.Contains("OK", StringComparison.OrdinalIgnoreCase)
                || content.Contains("success", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TestConnectionAsync(AccessControlDevice device)
    {
        var url = $"http://{device.IPAddress}/cgi-bin/configManager.cgi?action=getConfig&name=Network";

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(device.Username, device.Password),
            PreAuthenticate = true
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode;
    }
}
