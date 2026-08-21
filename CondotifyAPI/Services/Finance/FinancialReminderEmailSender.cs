using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Observability;

namespace CondotifyAPI.Services.Finance;

public sealed record FinancialEmailSendResult(bool Success, string Error)
{
    public static FinancialEmailSendResult Delivered() => new(true, string.Empty);
    public static FinancialEmailSendResult Failed(string error) => new(false, error);
}
public interface IFinancialReminderEmailSender
{
    bool IsReady(AlertNotificationPolicyDTO? policy);
    Task<FinancialEmailSendResult> SendAsync(
        AlertNotificationPolicyDTO? policy,
        string recipient,
        string residentName,
        string licenseName,
        string reference,
        string stageLabel,
        DateTime dueDate,
        CancellationToken cancellationToken = default);
}

public sealed class FinancialReminderEmailSender(
    IConfiguration configuration,
    ILogger<FinancialReminderEmailSender> logger) : IFinancialReminderEmailSender
{
    public bool IsReady(AlertNotificationPolicyDTO? policy) => Resolve(policy) is not null;

    public async Task<FinancialEmailSendResult> SendAsync(
        AlertNotificationPolicyDTO? policy,
        string recipient,
        string residentName,
        string licenseName,
        string reference,
        string stageLabel,
        DateTime dueDate,
        CancellationToken cancellationToken = default)
    {
        var settings = Resolve(policy);
        if (settings is null) return FinancialEmailSendResult.Failed("O transporte SMTP não está configurado.");
        MailAddress destination;
        try { destination = new MailAddress(recipient); }
        catch (FormatException) { return FinancialEmailSendResult.Failed("O e-mail do morador é inválido."); }

        var encoder = HtmlEncoder.Default;
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = $"F&F Access · {stageLabel}",
            IsBodyHtml = true,
            Body = $"""
                <div style="font-family:Inter,Arial,sans-serif;color:#1d2635;max-width:620px;line-height:1.55">
                  <p style="font-size:12px;font-weight:700;color:#092557;text-transform:uppercase">F&amp;F Access · Gestão financeira</p>
                  <h2 style="font-size:21px;margin:10px 0">{encoder.Encode(stageLabel)}</h2>
                  <p>Olá, {encoder.Encode(residentName)}.</p>
                  <p>Há uma atualização referente a <strong>{encoder.Encode(reference)}</strong> da sua unidade em {encoder.Encode(licenseName)}.</p>
                  <p>Vencimento informado: <strong>{dueDate.ToCondotifyTime():dd/MM/yyyy}</strong>.</p>
                  <div style="margin:22px 0;padding:14px 16px;border-radius:12px;background:#f1f5ff;color:#34435a">
                    Por segurança, valores e demais detalhes ficam disponíveis somente dentro do aplicativo Condotify.
                  </div>
                  <p>Abra o aplicativo para consultar a cobrança ou registrar uma manifestação.</p>
                </div>
                """
        };
        message.To.Add(destination);
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
            await smtp.SendMailAsync(message, cancellationToken);
            return FinancialEmailSendResult.Delivered();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao enviar lembrete financeiro para {Destination}", Mask(recipient));
            return FinancialEmailSendResult.Failed(Short(exception.Message, 1000));
        }
    }

    private SmtpSettings? Resolve(AlertNotificationPolicyDTO? policy)
    {
        if (policy is not null && !string.IsNullOrWhiteSpace(policy.SmtpHost) &&
            !string.IsNullOrWhiteSpace(policy.SmtpFromEmail) && policy.SmtpPort is > 0 and <= 65535)
            return new SmtpSettings(policy.SmtpHost.Trim(), policy.SmtpPort, policy.SmtpUsername,
                policy.SmtpPassword, policy.SmtpFromEmail, string.IsNullOrWhiteSpace(policy.SmtpFromName) ? "F&F Access" : policy.SmtpFromName,
                policy.SmtpEnableSsl);

        var host = Get("CONDOTIFY_SMTP_HOST", "Notifications:Smtp:Host");
        var from = Get("CONDOTIFY_SMTP_FROM_EMAIL", "Notifications:Smtp:FromEmail");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return null;
        var port = int.TryParse(Get("CONDOTIFY_SMTP_PORT", "Notifications:Smtp:Port"), out var parsedPort) ? parsedPort : 587;
        var ssl = !bool.TryParse(Get("CONDOTIFY_SMTP_ENABLE_SSL", "Notifications:Smtp:EnableSsl"), out var parsedSsl) || parsedSsl;
        return new SmtpSettings(host, port,
            Get("CONDOTIFY_SMTP_USERNAME", "Notifications:Smtp:Username") ?? string.Empty,
            Get("CONDOTIFY_SMTP_PASSWORD", "Notifications:Smtp:Password") ?? string.Empty,
            from, Get("CONDOTIFY_SMTP_FROM_NAME", "Notifications:Smtp:FromName") ?? "F&F Access", ssl);
    }

    private string? Get(string environmentName, string configurationName) =>
        Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationName];

    internal static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "e-mail protegido";
        var name = email[..at];
        return $"{name[0]}***@{email[(at + 1)..]}";
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
    private sealed record SmtpSettings(string Host, int Port, string Username, string Password, string FromEmail, string FromName, bool EnableSsl);
}
