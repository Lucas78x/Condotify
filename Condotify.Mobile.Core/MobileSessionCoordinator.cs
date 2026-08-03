using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Condotify.Services;

namespace Condotify.Mobile.Core;

public sealed class MobileSessionCoordinator : ISessionContextProvider
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IHttpClientFactory _clients;
    private readonly IMobileSessionVault _vault;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private MobileSession? _session;

    public MobileSessionCoordinator(
        IHttpClientFactory clients,
        IMobileSessionVault vault,
        TimeProvider? time = null)
    {
        _clients = clients;
        _vault = vault;
        _time = time ?? TimeProvider.System;
    }

    public MobileSession? Current => _session;
    public bool IsAuthenticated => _session?.IsAuthenticated == true;
    public event Action? Changed;

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        _session = await _vault.LoadAsync(cancellationToken);
        if (_session is not null && !await EnsureFreshAsync(cancellationToken))
            await ClearAsync(cancellationToken);
        Changed?.Invoke();
    }

    public Task<MobileLoginResult> LoginStaffAsync(
        string email,
        string password,
        string deviceLabel,
        CancellationToken cancellationToken = default) =>
        LoginAsync("api/auth/login", MobilePrincipalKind.Staff, email, password, deviceLabel, cancellationToken);

    public Task<MobileLoginResult> LoginResidentAsync(
        string email,
        string password,
        string deviceLabel,
        CancellationToken cancellationToken = default) =>
        LoginAsync("api/auth/resident/login", MobilePrincipalKind.Resident, email, password, deviceLabel, cancellationToken);

    public async Task<MobileLoginResult> VerifyStaffMfaAsync(
        string challengeToken,
        string code,
        string deviceLabel,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "api/auth/mfa/verify",
            new { ChallengeToken = challengeToken, Code = code, DeviceLabel = deviceLabel },
            cancellationToken);
        return await CompleteLoginAsync(response, MobilePrincipalKind.Staff, cancellationToken);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "api/auth/resident/password/forgot",
            new { Email = email.Trim() },
            cancellationToken);
    }

    public async Task<MobilePasswordResetResult> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "api/auth/resident/password/reset",
            new { Token = token.Trim(), NewPassword = newPassword },
            cancellationToken);
        using (response)
        {
            ResetPasswordResponse? payload = null;
            try { payload = await response.Content.ReadFromJsonAsync<ResetPasswordResponse>(JsonOptions, cancellationToken); }
            catch (JsonException) { }

            return response.IsSuccessStatusCode && payload?.Result == "Success"
                ? new MobilePasswordResetResult(true, string.Empty)
                : new MobilePasswordResetResult(false, payload?.Error ?? "Nao foi possivel redefinir a senha agora.");
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        try
        {
            if (session is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout")
                {
                    Content = JsonContent.Create(new { session.RefreshToken }, options: JsonOptions)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                await _clients.CreateClient("CondotifyAuth").SendAsync(request, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
        }
        finally
        {
            await ClearAsync(CancellationToken.None);
        }
    }

    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        await EnsureFreshAsync(cancellationToken) ? _session?.AccessToken : null;

    public ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_session?.EnterpriseId?.ToString());

    internal async Task<bool> EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null) return false;
        if (_session.AccessTokenExpiresAt > _time.GetUtcNow().AddMinutes(1)) return true;

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_session is null) return false;
            if (_session.AccessTokenExpiresAt > _time.GetUtcNow().AddMinutes(1)) return true;

            var response = await SendAsync(
                HttpMethod.Post,
                "api/auth/refresh",
                new { _session.RefreshToken },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ClearAsync(CancellationToken.None);
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken);
            if (payload?.Result != "Success" ||
                string.IsNullOrWhiteSpace(payload.AccessToken) ||
                string.IsNullOrWhiteSpace(payload.RefreshToken))
            {
                await ClearAsync(CancellationToken.None);
                return false;
            }

            _session = BuildSession(payload, _session.Principal, _session);
            await _vault.SaveAsync(_session, cancellationToken);
            Changed?.Invoke();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return _session?.AccessTokenExpiresAt > _time.GetUtcNow() == true;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<MobileLoginResult> LoginAsync(
        string path,
        MobilePrincipalKind principal,
        string email,
        string password,
        string deviceLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return new(false, false, "Informe email e senha.");
        var response = await SendAsync(
            HttpMethod.Post,
            path,
            new { Email = email.Trim(), Password = password, DeviceLabel = deviceLabel },
            cancellationToken);
        return await CompleteLoginAsync(response, principal, cancellationToken);
    }

    private async Task<MobileLoginResult> CompleteLoginAsync(
        HttpResponseMessage response,
        MobilePrincipalKind principal,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            AuthResponse? payload = null;
            try { payload = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken); }
            catch (JsonException) { }

            if (payload?.MfaRequired == true)
                return new(false, true, string.Empty, payload.ChallengeToken ?? string.Empty);
            if (!response.IsSuccessStatusCode || payload?.Result != "Success" ||
                string.IsNullOrWhiteSpace(payload.AccessToken) ||
                string.IsNullOrWhiteSpace(payload.RefreshToken))
                return new(false, false, response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Email ou senha invalidos."
                    : "Nao foi possivel entrar agora.");

            _session = BuildSession(payload, principal, null);
            await _vault.SaveAsync(_session, cancellationToken);
            Changed?.Invoke();
            return new(true, false, string.Empty);
        }
    }

    private MobileSession BuildSession(AuthResponse payload, MobilePrincipalKind principal, MobileSession? previous)
    {
        var claims = ReadClaims(payload.AccessToken!);
        var subjectId = Guid.TryParse(Value(claims, "sub", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"), out var id)
            ? id
            : payload.ResidentId ?? previous?.SubjectId ?? Guid.Empty;
        var enterprise = Guid.TryParse(Value(claims, "enterprise_id"), out var enterpriseId)
            ? enterpriseId
            : previous?.EnterpriseId;
        var license = payload.LicenseId ??
            (Guid.TryParse(Value(claims, "license_id"), out var licenseId) ? licenseId : previous?.LicenseId);
        var expiresAt = ReadExpiry(payload.AccessToken!) ?? _time.GetUtcNow().AddSeconds(payload.ExpiresIn ?? 3600);

        return new MobileSession(
            principal,
            payload.AccessToken!,
            payload.RefreshToken!,
            expiresAt,
            subjectId,
            payload.Name ?? Value(claims, "name") ?? previous?.Name ?? string.Empty,
            payload.Email ?? Value(claims, "email", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress") ?? previous?.Email ?? string.Empty,
            enterprise,
            license,
            payload.LicenseName ?? previous?.LicenseName ?? string.Empty);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        return await _clients.CreateClient("CondotifyAuth").SendAsync(request, cancellationToken);
    }

    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        _session = null;
        await _vault.ClearAsync(cancellationToken);
        Changed?.Invoke();
    }

    private static Dictionary<string, string> ReadClaims(string token)
    {
        try
        {
            using var payload = DecodePayload(token);
            return payload.RootElement.EnumerateObject()
                .Where(x => x.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                .ToDictionary(
                    x => x.Name,
                    x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() ?? string.Empty : x.Value.GetRawText(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static DateTimeOffset? ReadExpiry(string token)
    {
        try
        {
            using var payload = DecodePayload(token);
            return payload.RootElement.TryGetProperty("exp", out var expires) && expires.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static JsonDocument DecodePayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) throw new FormatException("JWT invalido.");
        var value = parts[1].Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(value));
    }

    private static string? Value(IReadOnlyDictionary<string, string> claims, params string[] keys)
    {
        foreach (var key in keys)
            if (claims.TryGetValue(key, out var value)) return value;
        return null;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class AuthResponse
    {
        public string Result { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public long? ExpiresIn { get; set; }
        public bool MfaRequired { get; set; }
        public string? ChallengeToken { get; set; }
        public Guid? ResidentId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public Guid? LicenseId { get; set; }
        public string? LicenseName { get; set; }
    }

    private sealed class ResetPasswordResponse
    {
        public string Result { get; set; } = string.Empty;
        public string? Error { get; set; }
    }
}
