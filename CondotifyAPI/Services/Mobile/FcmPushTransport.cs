using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CondotifyAPI.Services.Mobile;

public sealed record PushTransportMessage(
    string Title,
    string Body,
    string Route,
    string DeepLink,
    string Category,
    IReadOnlyDictionary<string, string> Data);

public enum PushTransportOutcome
{
    Delivered,
    InvalidToken,
    TransientFailure,
    PermanentFailure,
    Unavailable
}

public sealed record PushTransportResult(
    PushTransportOutcome Outcome,
    int? ResponseCode,
    string ProviderMessageId,
    string Error)
{
    public static PushTransportResult Delivered(string id, int code = 200) =>
        new(PushTransportOutcome.Delivered, code, id, string.Empty);
}

public interface IPushTransport
{
    bool IsConfigured { get; }
    Task<PushTransportResult> SendAsync(
        string pushToken,
        PushTransportMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class FcmPushTransport : IPushTransport
{
    private readonly IHttpClientFactory _clients;
    private readonly FcmAccessTokenProvider _tokens;
    private readonly ILogger<FcmPushTransport> _logger;

    public FcmPushTransport(
        IHttpClientFactory clients,
        FcmAccessTokenProvider tokens,
        ILogger<FcmPushTransport> logger)
    {
        _clients = clients;
        _tokens = tokens;
        _logger = logger;
    }

    public bool IsConfigured => _tokens.IsConfigured;

    public async Task<PushTransportResult> SendAsync(
        string pushToken,
        PushTransportMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return new(PushTransportOutcome.Unavailable, null, string.Empty, "FCM não configurado.");

        try
        {
            var accessToken = await _tokens.GetAsync(cancellationToken);
            if (accessToken is null)
                return new(PushTransportOutcome.Unavailable, null, string.Empty, "Credencial FCM indisponível.");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"v1/projects/{Uri.EscapeDataString(_tokens.ProjectId!)}/messages:send");
            request.Headers.Authorization = new("Bearer", accessToken);
            request.Content = JsonContent.Create(BuildPayload(pushToken, message));

            using var response = await _clients.CreateClient("FcmPush")
                .SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Classify(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha de conexão com o FCM");
            return new(PushTransportOutcome.TransientFailure, null, string.Empty, "Falha de conexão com o FCM.");
        }
    }

    internal static object BuildPayload(string pushToken, PushTransportMessage message)
    {
        var data = new Dictionary<string, string>(message.Data, StringComparer.Ordinal)
        {
            ["route"] = message.Route,
            ["deepLink"] = message.DeepLink,
            ["category"] = message.Category
        };

        return new
        {
            message = new
            {
                token = pushToken,
                notification = new { title = message.Title, body = message.Body },
                data,
                android = new { priority = "high" },
                apns = new
                {
                    headers = new Dictionary<string, string> { ["apns-priority"] = "10" },
                    payload = new { aps = new { sound = "default" } }
                }
            }
        };
    }

    internal static PushTransportResult Classify(HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode is >= 200 and < 300)
            return PushTransportResult.Delivered(ReadString(body, "name"), (int)statusCode);

        var providerCode = ReadFcmErrorCode(body);
        if (providerCode.Equals("UNREGISTERED", StringComparison.OrdinalIgnoreCase))
            return new(PushTransportOutcome.InvalidToken, (int)statusCode, string.Empty, "Instalação não registrada no FCM.");

        if (statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500)
            return new(PushTransportOutcome.TransientFailure, (int)statusCode, string.Empty, "FCM temporariamente indisponível.");

        return new(PushTransportOutcome.PermanentFailure, (int)statusCode, string.Empty, Short(ReadError(body), 500));
    }

    private static string ReadFcmErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error)
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
                return string.Empty;

            foreach (var detail in details.EnumerateArray())
                if (detail.TryGetProperty("errorCode", out var code))
                    return code.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
        }
        return string.Empty;
    }

    private static string ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
                return message.GetString() ?? "FCM recusou a mensagem.";
        }
        catch (JsonException)
        {
        }
        return "FCM recusou a mensagem.";
    }

    private static string ReadString(string body, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty(property, out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}

public sealed class FcmAccessTokenProvider
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<FcmAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly FcmServiceAccount? _account;
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public FcmAccessTokenProvider(
        IHttpClientFactory clients,
        IConfiguration configuration,
        ILogger<FcmAccessTokenProvider> logger)
    {
        _clients = clients;
        _logger = logger;
        _account = LoadAccount(configuration);
        ProjectId = Environment.GetEnvironmentVariable("CONDOTIFY_FCM_PROJECT_ID")
            ?? configuration["Push:Fcm:ProjectId"]
            ?? _account?.ProjectId;
    }

    public string? ProjectId { get; }
    public bool IsConfigured => _account is not null && !string.IsNullOrWhiteSpace(ProjectId);

    public async Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;
        if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return _accessToken;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
                return _accessToken;

            var assertion = CreateAssertion(_account!, DateTimeOffset.UtcNow);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion
            });
            using var response = await _clients.CreateClient("FcmOAuth")
                .PostAsync(_account!.TokenUri, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OAuth do FCM respondeu HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            using var json = JsonDocument.Parse(body);
            _accessToken = json.RootElement.GetProperty("access_token").GetString();
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 3600;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn));
            return _accessToken;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Não foi possível obter credencial OAuth do FCM");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string CreateAssertion(FcmServiceAccount account, DateTimeOffset now)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = account.ClientEmail,
            scope = Scope,
            aud = account.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(55).ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Base64Url(signature)}";
    }

    private static FcmServiceAccount? LoadAccount(IConfiguration configuration)
    {
        var path = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")
            ?? configuration["Push:Fcm:ServiceAccountPath"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new FcmServiceAccount(
                root.GetProperty("project_id").GetString() ?? string.Empty,
                root.GetProperty("client_email").GetString() ?? string.Empty,
                root.GetProperty("private_key").GetString() ?? string.Empty,
                root.TryGetProperty("token_uri", out var tokenUri)
                    ? tokenUri.GetString() ?? "https://oauth2.googleapis.com/token"
                    : "https://oauth2.googleapis.com/token");
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record FcmServiceAccount(string ProjectId, string ClientEmail, string PrivateKey, string TokenUri);
