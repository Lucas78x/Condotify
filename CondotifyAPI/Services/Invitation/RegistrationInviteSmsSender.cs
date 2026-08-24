using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Condotify.Models;

namespace CondotifyAPI.Services.Invitation;

public sealed record RegistrationInviteSmsResult(bool Success, string Error, string ProviderMessageId)
{
    public static RegistrationInviteSmsResult Queued(string providerMessageId) =>
        new(true, string.Empty, providerMessageId);

    public static RegistrationInviteSmsResult Failed(string error) =>
        new(false, error, string.Empty);
}

public interface IRegistrationInviteSmsSender
{
    bool IsReady();
    bool IsValidRecipient(string recipient);

    Task<RegistrationInviteSmsResult> SendAsync(
        string recipient,
        string residentName,
        string licenseName,
        string inviteUrl,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Envia convites por SMS pela API REST da Twilio. Credenciais, corpo e link do
/// convite nunca são incluídos nos logs.
/// </summary>
public sealed class RegistrationInviteSmsSender(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<RegistrationInviteSmsSender> logger) : IRegistrationInviteSmsSender
{
    public bool IsReady() => Resolve() is not null;

    public bool IsValidRecipient(string recipient) =>
        TryNormalizePhone(recipient, DefaultCountryCode(), out _);

    public async Task<RegistrationInviteSmsResult> SendAsync(
        string recipient,
        string residentName,
        string licenseName,
        string inviteUrl,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        var settings = Resolve();
        if (settings is null)
            return RegistrationInviteSmsResult.Failed("O envio por SMS ainda não está configurado.");

        if (!TryNormalizePhone(recipient, settings.DefaultCountryCode, out var destination))
            return RegistrationInviteSmsResult.Failed("Informe um celular válido com DDD.");

        if (!Uri.TryCreate(inviteUrl, UriKind.Absolute, out var inviteUri) ||
            !string.Equals(inviteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return RegistrationInviteSmsResult.Failed(
                "O endereço público HTTPS do portal não está configurado.");

        var values = new Dictionary<string, string>
        {
            ["To"] = destination,
            ["Body"] = BuildMessage(residentName, licenseName, inviteUrl, expiresAt)
        };
        if (!string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            values["MessagingServiceSid"] = settings.MessagingServiceSid;
        else
            values["From"] = settings.FromNumber;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"2010-04-01/Accounts/{settings.AccountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(values)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(
                $"{settings.ApiKeySid}:{settings.ApiKeySecret}")));

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Twilio recusou convite SMS para {Destination}; HTTP {StatusCode}",
                    Mask(destination),
                    (int)response.StatusCode);
                return RegistrationInviteSmsResult.Failed(
                    "Não foi possível enviar o convite por SMS. Verifique o número e tente novamente.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var sid = document.RootElement.TryGetProperty("sid", out var sidElement)
                ? sidElement.GetString() ?? string.Empty
                : string.Empty;
            return RegistrationInviteSmsResult.Queued(sid);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(
                exception,
                "Falha de comunicação com a Twilio ao enviar convite SMS para {Destination}",
                Mask(destination));
            return RegistrationInviteSmsResult.Failed(
                "O serviço de SMS está temporariamente indisponível. Tente novamente.");
        }
    }

    internal static string BuildMessage(
        string residentName,
        string licenseName,
        string inviteUrl,
        DateTime expiresAt)
    {
        var name = CleanSingleLine(residentName, 40, "Morador");
        var condominium = CleanSingleLine(licenseName, 50, "seu condomínio");
        return $"Olá, {name}! {condominium} convidou você para o F&F Access. " +
               $"Cadastre seu acesso: {inviteUrl} Válido até {expiresAt.ToCondotifyTime():dd/MM HH:mm}. " +
               "Não compartilhe este link.";
    }

    internal static bool TryNormalizePhone(string? value, string defaultCountryCode, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character => !char.IsDigit(character) &&
                                   !char.IsWhiteSpace(character) &&
                                   character is not '+' and not '-' and not '(' and not ')' and not '.'))
            return false;

        var trimmed = value.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (trimmed.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];
        else if (!trimmed.StartsWith('+') && digits.Length is 10 or 11)
            digits = $"{NormalizeCountryCode(defaultCountryCode)}{digits}";

        if (digits.Length is < 8 or > 15 || digits[0] == '0') return false;
        normalized = $"+{digits}";
        return true;
    }

    internal static string Mask(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length < 4 ? "celular protegido" : $"***{digits[^4..]}";
    }

    private SmsSettings? Resolve()
    {
        var accountSid = Get("CONDOTIFY_TWILIO_ACCOUNT_SID", "Twilio:AccountSid")?.Trim();
        var apiKeySid = Get("CONDOTIFY_TWILIO_API_KEY_SID", "Twilio:ApiKeySid")?.Trim();
        var apiKeySecret = Get("CONDOTIFY_TWILIO_API_KEY_SECRET", "Twilio:ApiKeySecret")?.Trim();
        var messagingServiceSid = Get(
            "CONDOTIFY_TWILIO_MESSAGING_SERVICE_SID",
            "Twilio:MessagingServiceSid")?.Trim() ?? string.Empty;
        var configuredFrom = Get("CONDOTIFY_TWILIO_FROM_NUMBER", "Twilio:FromNumber")?.Trim() ?? string.Empty;
        var countryCode = DefaultCountryCode();

        if (!IsSid(accountSid, "AC") || !IsSid(apiKeySid, "SK") ||
            string.IsNullOrWhiteSpace(apiKeySecret) || apiKeySecret.Length < 16)
            return null;

        if (!string.IsNullOrWhiteSpace(messagingServiceSid))
        {
            if (!IsSid(messagingServiceSid, "MG")) return null;
        }
        else if (!TryNormalizePhone(configuredFrom, countryCode, out configuredFrom))
        {
            return null;
        }

        return new SmsSettings(
            accountSid!,
            apiKeySid!,
            apiKeySecret,
            configuredFrom,
            messagingServiceSid,
            countryCode);
    }

    private string DefaultCountryCode() => NormalizeCountryCode(
        Get("CONDOTIFY_SMS_DEFAULT_COUNTRY_CODE", "Twilio:DefaultCountryCode") ?? "55");

    private string? Get(string environmentName, string configurationName) =>
        Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationName];

    private static bool IsSid(string? value, string prefix) =>
        value is { Length: 34 } && value.StartsWith(prefix, StringComparison.Ordinal) &&
        value[2..].All(Uri.IsHexDigit);

    private static string NormalizeCountryCode(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length is >= 1 and <= 3 ? digits : "55";
    }

    private static string CleanSingleLine(string value, int maxLength, string fallback)
    {
        var cleaned = (string.IsNullOrWhiteSpace(value) ? fallback : value)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private sealed record SmsSettings(
        string AccountSid,
        string ApiKeySid,
        string ApiKeySecret,
        string FromNumber,
        string MessagingServiceSid,
        string DefaultCountryCode);
}
