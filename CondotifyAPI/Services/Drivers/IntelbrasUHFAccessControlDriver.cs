using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Drivers;
using CondotifyAPI.Services.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;

public class IntelbrasUHFAccessControlDriver : IAccessControlDriver
{
    private readonly IHttpClientFactory _clientFactory;

    public bool Supports(DeviceTypeEnum type) => type.IsInIntelbrasUHF();

    public IntelbrasUHFAccessControlDriver(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }


    #region Commands
    public async Task<bool> TestConnectionAsync(CreateAccessControlDeviceByLicenseIn device)
    {
        try
        {
            var url = $"http://{device.IPAddress}/cgi-bin/global.cgi?action=getCurrentTime";

            var handler = new HttpClientHandler()
            {
                PreAuthenticate = true,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(device.Username, device.Password)
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(url);
           var success = response.IsSuccessStatusCode;

            if (!success)
                return false;

            var version = await GetFirmwareVersionAsync(device.IPAddress,device.Username,device.Password);

            Console.WriteLine($"Device:{device.Type.ToString()} Firmware Version:{version}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> OpenDoorAsync(
    AccessControlDevice device,
    int channel,
    string? userId = null,
    string? type = null,
    int? time = null)
    {
        try
        {
            var query = $"action=openDoor&channel={channel}";

            if (!string.IsNullOrWhiteSpace(userId))
                query += $"&UserID={userId}";

            if (!string.IsNullOrWhiteSpace(type))
                query += $"&Type={type}";

            if (time.HasValue)
                query += $"&Time={time.Value}";

            var url = $"http://{device.IPAddress}/cgi-bin/accessControl.cgi?{query}";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(device.Username, device.Password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(); 
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> CloseDoorAsync(AccessControlDevice device, int channel)
    {
        try
        {
            var url =
                $"http://{device.IPAddress}/cgi-bin/accessControl.cgi?action=closeDoor&channel={channel}";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(device.Username, device.Password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Insert
    public async Task<string?> InsertUsersAsync(
    AccessControlDevice device,
    List<AccessUserCreate> users)
    {
        var url = $"http://{device.IPAddress}/cgi-bin/AccessUser.cgi?action=insertMulti";

        var wrapper = new UserListWrapper<AccessUserCreate>
        {
            UserList = users
        };

        return await DigestPostJsonAsync(url, device.Username, device.Password, wrapper);
    }

    public async Task<string?> InsertCardsAsync(
    AccessControlDevice device,
    List<AccessCard> cards)
    {
        var url = $"http://{device.IPAddress}/cgi-bin/AccessCard.cgi?action=insertMulti";

        var wrapper = new CardListWrapper
        {
            CardList = cards
        };

        return await DigestPostJsonAsync(url, device.Username, device.Password, wrapper);
    }
    private async Task<string?> DigestPostJsonAsync(string url, string username, string password, object jsonBody)
    {
        try
        {
            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(username, password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);

            var json = JsonSerializer.Serialize(jsonBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Update
    public async Task<string?> UpdateUsersAsync(
    AccessControlDevice device,
    List<AccessUserUpdate> users)
    {
        var url = $"http://{device.IPAddress}/cgi-bin/AccessUser.cgi?action=updateMulti";

        var wrapper = new UserListWrapper<AccessUserUpdate>
        {
            UserList = users
        };

        return await DigestPostJsonAsync(url, device.Username, device.Password, wrapper);
    }
    #endregion

    #region Get
    public async Task<string?> GetFirmwareVersionAsync(string ip, string username, string password)
    {
        try
        {
            var url = $"http://{ip}/cgi-bin/magicBox.cgi?action=getSoftwareVersion";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(username, password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<AccessUser>> GetUsersAsync(AccessControlDevice device)
    {
        var baseUrl = $"http://{device.IPAddress}";
        var users = new List<AccessUser>();

        try
        {
            var startUrl = $"{baseUrl}/cgi-bin/AccessUser.cgi?action=startFind";
            var startJson = await DigestGetAsync(startUrl, device.Username, device.Password);

            if (string.IsNullOrWhiteSpace(startJson))
                return users;

            var start = JsonSerializer.Deserialize<StartFindResponse>(startJson);

            if (start == null || start.Total == 0)
            {
                var stopUrl0 = $"{baseUrl}/cgi-bin/AccessUser.cgi?action=stopFind";
                await DigestGetAsync(stopUrl0, device.Username, device.Password);
                return users;
            }

            int totalUsers = start.Total;
            int countPerRequest = 5;

            while (users.Count < totalUsers)
            {
                var doFindUrl =
                    $"{baseUrl}/cgi-bin/AccessUser.cgi?action=doFind&Count={countPerRequest}";

                var json = await DigestGetAsync(doFindUrl, device.Username, device.Password);

                if (string.IsNullOrWhiteSpace(json))
                    break;

                var block = JsonSerializer.Deserialize<DoFindResponse>(json);

                if (block?.Info == null || block.Info.Count == 0)
                    break;

                users.AddRange(block.Info);
            }

            var stopUrl = $"{baseUrl}/cgi-bin/AccessUser.cgi?action=stopFind";
            await DigestGetAsync(stopUrl, device.Username, device.Password);

            return users;
        }
        catch
        {
            return users;
        }
    }

    public async Task<int?> GetCardsCountAsync(AccessControlDevice device)
    {
        try
        {
            var url =
                $"http://{device.IPAddress}/cgi-bin/recordFinder.cgi?action=getQuerySize&name=AccessCardInfo";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(device.Username, device.Password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var text = await response.Content.ReadAsStringAsync();

            if (text.StartsWith("count="))
            {
                if (int.TryParse(text.Replace("count=", "").Trim(), out int count))
                    return count;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
    public async Task GetEventsAsync(string ip, string username, string password)
    {
        var url = $"http://{ip}/cgi-bin/eventManager.cgi?action=attach&codes=[All]&heartbeat=5";

        var credentialCache = new CredentialCache();
        credentialCache.Add(
            new Uri(url),
            "Digest",
            new NetworkCredential(username, password)
        );

        var handler = new HttpClientHandler()
        {
            Credentials = credentialCache,
            PreAuthenticate = true
        };

        using var client = new HttpClient(handler);
        client.Timeout = Timeout.InfiniteTimeSpan; 

        Console.WriteLine("Conectando ao EventManager...");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        Console.WriteLine($"STATUS: {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Erro: {await response.Content.ReadAsStringAsync()}");
            return;
        }

        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        Console.WriteLine("📡 Lendo eventos... (Ctrl+C para parar)");

        string? line;
        var builder = new StringBuilder();

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (builder.Length > 0)
                {
                    Console.WriteLine("\n🔔 EVENTO RECEBIDO:");
                    Console.WriteLine(builder.ToString());
                    builder.Clear();
                }
            }
            else if (!line.StartsWith("--myboundary")) 
            {
                builder.AppendLine(line);
            }
        }
    }

    private async Task<string?> DigestGetAsync(string url, string username, string password)
    {
        try
        {
            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(username, password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Remove
    public async Task<string?> RemoveUserAsync(string ip, string username, string password, int userId)
    {
        try
        {
            var url =
                $"http://{ip}/cgi-bin/AccessUser.cgi?action=removeMulti&UserIDList[0]={userId}";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(username, password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }
    public async Task<string?> RemoveCardAsync(AccessControlDevice device, string cardNo)
    {
        try
        {
            cardNo = cardNo.Trim().Replace(" ", "").ToUpper();

            var url =
                $"http://{device.IPAddress}/cgi-bin/AccessCard.cgi?action=removeMulti&CardNoList[0]={cardNo}";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(device.Username, device.Password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }
    public async Task<string?> RemoveMultipleCardsAsync(AccessControlDevice device, List<string> cardNos)
    {
        try
        {
            for (int i = 0; i < cardNos.Count; i++)
                cardNos[i] = cardNos[i].Trim().Replace(" ", "").ToUpper();

            var parameters = string.Join("&",
                cardNos.Select((c, i) => $"CardNoList[{i}]={c}")
            );

            var url =
                $"http://{device.IPAddress}/cgi-bin/AccessCard.cgi?action=removeMulti&{parameters}";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(device.Username, device.Password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }
    public async Task<string?> RemoveAllUsersAsync(AccessControlDevice device)
    {
        try
        {
            var url =
                $"http://{device.IPAddress}/cgi-bin/AccessUser.cgi?action=removeAll";

            var credentialCache = new CredentialCache();
            credentialCache.Add(
                new Uri(url),
                "Digest",
                new NetworkCredential(device.Username, device.Password)
            );

            var handler = new HttpClientHandler()
            {
                Credentials = credentialCache,
                PreAuthenticate = true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(); 
        }
        catch
        {
            return null;
        }
    }
    #endregion

    public Task<bool> AddUserAsync(AccessControlDevice device, object user)
        => throw new NotImplementedException();

    public Task<bool> DeleteUserAsync(AccessControlDevice device, string userId)
        => throw new NotImplementedException();

    public Task<string> GetEventsAsync(AccessControlDevice device)
        => throw new NotImplementedException();

    Task<string> IAccessControlDriver.GetUsersAsync(AccessControlDevice device)
    {
        throw new NotImplementedException();
    }

    #region TODO: Será refeito no User
    public class AccessUser
    {
        public string UserID { get; set; }
        public string UserName { get; set; }
        public int UserType { get; set; }
        public bool IsFirstEnter { get; set; }
        public int UserStatus { get; set; }
        public string CitizenIDNo { get; set; }
        public List<int> SpecialDaysSchedule { get; set; }
        public string Password { get; set; }
        public List<int> Doors { get; set; }
        public List<int> TimeSections { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
    }
    public class AccessUserCreate
    {
        public string UserID { get; set; }
        public string UserName { get; set; }
        public int UserType { get; set; }
        public int UseTime { get; set; }
        public int UserStatus { get; set; }
        public int Authority { get; set; }
        public int[] Doors { get; set; }
        public int[] TimeSections { get; set; }
        public int[] SpecialDaysSchedule { get; set; }
        public string? Password { get; set; }
        public string ValidFrom { get; set; }
        public string ValidTo { get; set; }
    }

    public class AccessUserUpdate
    {
        public string UserID { get; set; }
        public string? UserName { get; set; }
        public int? UserType { get; set; }
        public int[] Doors { get; set; }
        public int[] TimeSections { get; set; }
        public int? Authority { get; set; }
        public string? Password { get; set; }
        public string? ValidFrom { get; set; }
        public string? ValidTo { get; set; }
    }

    public class UserListWrapper<T>
    {
        public List<T> UserList { get; set; }
    }
    public class StartFindResponse
    {
        public int Token { get; set; }
        public int Total { get; set; }
        public int Caps { get; set; }
    }

    public class DoFindResponse
    {
        public List<AccessUser> Info { get; set; }
    }

    public class AccessCard
    {
        public string UserID { get; set; }
        public string CardNo { get; set; }
        public int CardType { get; set; }
        public int CardStatus { get; set; }
    }

    public class CardListWrapper
    {
        public List<AccessCard> CardList { get; set; }
    }
    #endregion

}
