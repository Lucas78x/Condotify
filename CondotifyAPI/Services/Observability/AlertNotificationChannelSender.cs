using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Observability;

namespace CondotifyAPI.Services.Observability;

public sealed record AlertNotificationMessage(
    Guid AlertId,
    Guid LicenseId,
    string LicenseName,
    string Type,
    string Severity,
    string Title,
    string Message,
    string Status,
    int EscalationLevel,
    DateTime OccurredAt,
    string TargetUrl);

public sealed record NotificationSendResult(bool Success, int? ResponseCode, string Error)
{
    public static NotificationSendResult Delivered(int? responseCode = null) => new(true, responseCode, string.Empty);
    public static NotificationSendResult Failed(string error, int? responseCode = null) => new(false, responseCode, error);
}

public interface IAlertNotificationChannelSender
{
    bool IsEmailTransportReady(AlertNotificationPolicyDTO policy);
    string EmailTransportSource(AlertNotificationPolicyDTO policy);
    Task<NotificationSendResult> SendAsync(
        NotificationChannel channel,
        AlertNotificationPolicyDTO policy,
        AlertNotificationMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class AlertNotificationChannelSender(
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<AlertNotificationChannelSender> logger) : IAlertNotificationChannelSender
{
    public bool IsEmailTransportReady(AlertNotificationPolicyDTO policy) =>
        ResolveSmtp(policy) is not null;

    public string EmailTransportSource(AlertNotificationPolicyDTO policy) =>
        HasDatabaseSmtp(policy) ? "Web" : ResolveSmtp(policy) is not null ? "Environment" : string.Empty;

    public Task<NotificationSendResult> SendAsync(
        NotificationChannel channel,
        AlertNotificationPolicyDTO policy,
        AlertNotificationMessage message,
        CancellationToken cancellationToken = default) =>
        channel switch
        {
            NotificationChannel.Webhook => SendWebhookAsync(policy, message, cancellationToken),
            NotificationChannel.Email => SendEmailAsync(policy, message, cancellationToken),
            _ => Task.FromResult(NotificationSendResult.Failed("Canal de notificação não suportado."))
        };

    private async Task<NotificationSendResult> SendWebhookAsync(
        AlertNotificationPolicyDTO policy,
        AlertNotificationMessage message,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(policy.WebhookUrl, UriKind.Absolute, out var endpoint))
            return NotificationSendResult.Failed("Endpoint de webhook inválido.");

        var payload = JsonSerializer.Serialize(new
        {
            eventName = message.EscalationLevel == 0 ? "alert.opened" : "alert.escalated",
            sentAt = DateTime.UtcNow,
            alert = message
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-Condotify-Event", message.EscalationLevel == 0 ? "alert.opened" : "alert.escalated");
        request.Headers.TryAddWithoutValidation("X-Condotify-Delivery", $"{message.AlertId:N}:{message.EscalationLevel}");
        if (!string.IsNullOrWhiteSpace(policy.WebhookSecret))
            request.Headers.TryAddWithoutValidation("X-Condotify-Signature", Sign(payload, policy.WebhookSecret));

        try
        {
            using var response = await clients.CreateClient("AlertNotifications")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
                return NotificationSendResult.Delivered((int)response.StatusCode);
            return NotificationSendResult.Failed(
                $"Webhook respondeu HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao enviar webhook de alerta {AlertId}", message.AlertId);
            return NotificationSendResult.Failed("Falha de conexão com o endpoint de webhook.");
        }
    }

    private async Task<NotificationSendResult> SendEmailAsync(
        AlertNotificationPolicyDTO policy,
        AlertNotificationMessage message,
        CancellationToken cancellationToken)
    {
        var settings = ResolveSmtp(policy);
        if (settings is null)
            return NotificationSendResult.Failed("Transporte SMTP não configurado.");
        var recipients = ParseRecipients(policy.EmailRecipients);
        if (recipients.Count == 0)
            return NotificationSendResult.Failed("Nenhum destinatário válido configurado.");

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = $"[{SeverityLabel(message.Severity)}] {message.Title}",
            Body = BuildEmailBody(message),
            IsBodyHtml = true
        };
        foreach (var recipient in recipients)
            mail.To.Add(recipient);
        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = string.IsNullOrWhiteSpace(settings.Username),
            Credentials = string.IsNullOrWhiteSpace(settings.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(settings.Username, settings.Password)
        };

        try
        {
            await smtp.SendMailAsync(mail, cancellationToken);
            return NotificationSendResult.Delivered();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao enviar e-mail de alerta {AlertId}", message.AlertId);
            return NotificationSendResult.Failed(Short(exception.Message, 1000));
        }
    }

    internal static string Sign(string payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var body = Encoding.UTF8.GetBytes(payload);
        try
        {
            var hash = HMACSHA256.HashData(key, body);
            return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(body);
        }
    }

    internal static List<MailAddress> ParseRecipients(string value)
    {
        var result = new List<MailAddress>();
        foreach (var item in value.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { result.Add(new MailAddress(item)); }
            catch (FormatException) { }
        }
        return result;
    }

    private string? Get(string environmentName, string configurationName) =>
        Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationName];

    private SmtpTransportSettings? ResolveSmtp(AlertNotificationPolicyDTO policy)
    {
        if (HasDatabaseSmtp(policy))
            return new SmtpTransportSettings(
                policy.SmtpHost.Trim(),
                policy.SmtpPort,
                policy.SmtpUsername,
                policy.SmtpPassword,
                policy.SmtpFromEmail,
                string.IsNullOrWhiteSpace(policy.SmtpFromName) ? "F&F Access" : policy.SmtpFromName,
                policy.SmtpEnableSsl);

        var host = Get("CONDOTIFY_SMTP_HOST", "Notifications:Smtp:Host");
        var fromEmail = Get("CONDOTIFY_SMTP_FROM_EMAIL", "Notifications:Smtp:FromEmail");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
            return null;
        var port = int.TryParse(Get("CONDOTIFY_SMTP_PORT", "Notifications:Smtp:Port"), out var parsedPort)
            ? parsedPort
            : 587;
        var enableSsl = !bool.TryParse(
            Get("CONDOTIFY_SMTP_ENABLE_SSL", "Notifications:Smtp:EnableSsl"),
            out var parsedSsl) || parsedSsl;
        return new SmtpTransportSettings(
            host,
            port,
            Get("CONDOTIFY_SMTP_USERNAME", "Notifications:Smtp:Username") ?? string.Empty,
            Get("CONDOTIFY_SMTP_PASSWORD", "Notifications:Smtp:Password") ?? string.Empty,
            fromEmail,
            Get("CONDOTIFY_SMTP_FROM_NAME", "Notifications:Smtp:FromName") ?? "F&F Access",
            enableSsl);
    }

    private static bool HasDatabaseSmtp(AlertNotificationPolicyDTO policy) =>
        !string.IsNullOrWhiteSpace(policy.SmtpHost) &&
        !string.IsNullOrWhiteSpace(policy.SmtpFromEmail) &&
        policy.SmtpPort is > 0 and <= 65535;

    private static string BuildEmailBody(AlertNotificationMessage message)
    {
        var encoder = HtmlEncoder.Default;
        return $"""
            <div style="font-family:Arial,sans-serif;color:#202532;max-width:640px">
              <p style="font-size:12px;color:#092557;text-transform:uppercase">F&amp;F Access · Central de operações</p>
              <h2 style="font-size:20px">{encoder.Encode(message.Title)}</h2>
              <p>{encoder.Encode(message.Message)}</p>
              <table style="font-size:14px;border-collapse:collapse">
                <tr><td style="padding:4px 16px 4px 0;color:#657083">Condomínio</td><td>{encoder.Encode(message.LicenseName)}</td></tr>
                <tr><td style="padding:4px 16px 4px 0;color:#657083">Severidade</td><td>{encoder.Encode(SeverityLabel(message.Severity))}</td></tr>
                <tr><td style="padding:4px 16px 4px 0;color:#657083">Ocorrência</td><td>{message.OccurredAt.ToCondotifyTime():dd/MM/yyyy HH:mm}</td></tr>
                <tr><td style="padding:4px 16px 4px 0;color:#657083">Escalonamento</td><td>Nível {message.EscalationLevel}</td></tr>
              </table>
              <p style="margin-top:20px">Acesse a Central de Operações para reconhecer ou resolver este alerta.</p>
            </div>
            """;
    }

    private static string SeverityLabel(string severity) => severity switch
    {
        "Critical" => "Crítico",
        "Warning" => "Atenção",
        _ => "Informativo"
    };

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record SmtpTransportSettings(
        string Host,
        int Port,
        string Username,
        string Password,
        string FromEmail,
        string FromName,
        bool EnableSsl);
}
