using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace CondotifyAPI.Services.Security;

/// <summary>
/// Sends the "here is your recovery code" e-mail for task 8 ("forgot password"). This is
/// deliberately a small, separate sender rather than a reuse of
/// <see cref="Observability.IAlertNotificationChannelSender"/> - see the type doc below for
/// why that interface does not fit.
///
/// Every failure mode here (SMTP not configured, send throws) is absorbed internally and
/// never surfaces to the caller: <c>ResidentAuthController.ForgotPassword</c> must return 202
/// unconditionally, so this type has no way to make it do otherwise even if a caller forgot to
/// wrap the call.
/// </summary>
public interface IResidentPasswordRecoveryMailer
{
    Task SendAsync(string toEmail, string residentName, string recoveryCode, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="Observability.IAlertNotificationChannelSender"/> (read in full before writing
/// this) is shaped entirely around *operational alerts*: <c>SendAsync</c> requires an
/// <c>AlertNotificationPolicyDTO</c> (a per-licence distribution list plus SMTP settings meant
/// for the condominium's ops/admin recipients) and an <c>AlertNotificationMessage</c> whose
/// fields (AlertId, Severity, EscalationLevel, TargetUrl...) have no meaning for a password
/// reset. Worse, its email body template is hard-coded
/// (<c>AlertNotificationChannelSender.BuildEmailBody</c>, private) to render "Severidade",
/// "Escalonamento" and "Acesse a Central de Operacoes para reconhecer ou resolver este
/// alerta." - literally wrong, confusing copy to put in front of a resident who just asked to
/// reset their password. Contorting a policy's <c>EmailRecipients</c> field to smuggle in one
/// resident's personal address, and stuffing a recovery code into <c>Message</c> hoping nobody
/// reads the rest of the rendered table, would be reusing the type in name only. This sender
/// exists instead: same SMTP resolution shape (CONDOTIFY_SMTP_* environment variables /
/// appsettings, matching <c>AlertNotificationChannelSender.ResolveSmtp</c>'s environment
/// fallback exactly, so operators configure SMTP once) but with a body and recipient model
/// that actually fits a single transactional e-mail to one resident.
/// </summary>
public sealed class ResidentPasswordRecoveryMailer(
    IConfiguration configuration,
    ILogger<ResidentPasswordRecoveryMailer> logger) : IResidentPasswordRecoveryMailer
{
    public async Task SendAsync(string toEmail, string residentName, string recoveryCode, CancellationToken cancellationToken = default)
    {
        var settings = ResolveSmtp();
        if (settings is null)
        {
            // The visible behaviour (forgot always answers 202) must not change depending on
            // whether SMTP is configured - this is logged for operators, never surfaced.
            logger.LogInformation("Recuperacao de senha de morador solicitada, mas nenhum transporte SMTP esta configurado; nenhum e-mail foi enviado.");
            return;
        }

        MailAddress recipient;
        try
        {
            recipient = new MailAddress(toEmail);
        }
        catch (FormatException)
        {
            logger.LogWarning("Recuperacao de senha de morador: endereco de e-mail invalido, nenhum e-mail foi enviado.");
            return;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName),
            Subject = "Condotify - Codigo para redefinir sua senha",
            Body = BuildBody(residentName, recoveryCode),
            IsBodyHtml = true,
        };
        mail.To.Add(recipient);

        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = string.IsNullOrWhiteSpace(settings.Username),
            Credentials = string.IsNullOrWhiteSpace(settings.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(settings.Username, settings.Password),
        };

        try
        {
            await smtp.SendMailAsync(mail, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never let an SMTP failure escape - the caller (ForgotPassword) must always
            // return 202 regardless of whether sending actually succeeded. Neither the
            // recovery code nor any password is included in this log statement.
            logger.LogWarning(exception, "Falha ao enviar e-mail de recuperacao de senha de morador.");
        }
    }

    private static string BuildBody(string residentName, string recoveryCode)
    {
        var encoder = HtmlEncoder.Default;
        return $"""
            <div style="font-family:Arial,sans-serif;color:#202532;max-width:640px">
              <p style="font-size:12px;color:#657083;text-transform:uppercase">Condotify</p>
              <h2 style="font-size:20px">Redefinicao de senha</h2>
              <p>Ola, {encoder.Encode(residentName)}. Recebemos uma solicitacao para redefinir a senha da sua conta.</p>
              <p>Abra o aplicativo Condotify, toque em "Esqueci minha senha" e informe o codigo abaixo junto com a nova senha:</p>
              <p style="font-size:28px;font-weight:bold;letter-spacing:2px;margin:16px 0">{encoder.Encode(recoveryCode)}</p>
              <p>Este codigo e valido por 30 minutos e so pode ser usado uma vez.</p>
              <p style="margin-top:20px;color:#657083">Se voce nao solicitou isso, ignore este e-mail - sua senha continua a mesma.</p>
            </div>
            """;
    }

    private string? Get(string environmentName, string configurationName) =>
        Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationName];

    private SmtpTransportSettings? ResolveSmtp()
    {
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
            Get("CONDOTIFY_SMTP_FROM_NAME", "Notifications:Smtp:FromName") ?? "Condotify",
            enableSsl);
    }

    private sealed record SmtpTransportSettings(
        string Host,
        int Port,
        string Username,
        string Password,
        string FromEmail,
        string FromName,
        bool EnableSsl);
}
