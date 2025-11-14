using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Extensions;

namespace CondotifyAPI.Services.AccessControl
{
    public class AccessControlService : IAccessControlService
    {
        private readonly HttpClient _httpClient;

        public AccessControlService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device)
        {
            try
            {
                if (device.Type.IsIn())
                {
                    var url = $"http://{device.IPAddress}/cgi-bin/configManager.cgi?action=getConfig&name=Network";

                    var handler = new HttpClientHandler
                    {
                        Credentials = new NetworkCredential(device.Username, device.Password),
                        PreAuthenticate = true
                    };

                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);

                        var response = await client.GetAsync(url);

                        return response.IsSuccessStatusCode;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public Task<string> GetUsersAsync(AccessControlDevice device)
        {
            throw new NotImplementedException();
        }

        public Task<bool> AddUserAsync(AccessControlDevice device, object user)
        {
            throw new NotImplementedException();
        }

        public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetEventsAsync(AccessControlDevice device)
        {
            throw new NotImplementedException();
        }
    }
}
