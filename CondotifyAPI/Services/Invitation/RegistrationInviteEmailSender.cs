using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text.Encodings.Web;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Observability;

namespace CondotifyAPI.Services.Invitation;

public sealed record RegistrationInviteEmailResult(bool Success, string Error)
{
    public static RegistrationInviteEmailResult Delivered() => new(true, string.Empty);
    public static RegistrationInviteEmailResult Failed(string error) => new(false, error);
}

public interface IRegistrationInviteEmailSender
{
    bool IsReady(AlertNotificationPolicyDTO? policy);

    Task<RegistrationInviteEmailResult> SendAsync(
        AlertNotificationPolicyDTO? policy,
        string recipient,
        string residentName,
        string licenseName,
        string inviteUrl,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Envia convites de primeiro acesso usando o SMTP configurado para o condomínio.
/// O link nunca é registrado em log e o e-mail não carrega imagens ou rastreadores externos.
/// </summary>
public sealed class RegistrationInviteEmailSender(
    IConfiguration configuration,
    ILogger<RegistrationInviteEmailSender> logger) : IRegistrationInviteEmailSender
{
    public bool IsReady(AlertNotificationPolicyDTO? policy) => Resolve(policy) is not null;

    public async Task<RegistrationInviteEmailResult> SendAsync(
        AlertNotificationPolicyDTO? policy,
        string recipient,
        string residentName,
        string licenseName,
        string inviteUrl,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        var settings = Resolve(policy);
        if (settings is null)
            return RegistrationInviteEmailResult.Failed("O SMTP do condomínio não está configurado.");

        MailAddress destination;
        try
        {
            destination = new MailAddress(recipient);
            if (!string.Equals(destination.Address, recipient.Trim(), StringComparison.OrdinalIgnoreCase))
                return RegistrationInviteEmailResult.Failed("O e-mail informado é inválido.");
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return RegistrationInviteEmailResult.Failed("O e-mail informado é inválido.");
        }

        if (!Uri.TryCreate(inviteUrl, UriKind.Absolute, out var inviteUri) ||
            !string.Equals(inviteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return RegistrationInviteEmailResult.Failed("O endereço público HTTPS do portal não está configurado.");

        MailAddress origin;
        try
        {
            origin = new MailAddress(settings.FromEmail, settings.FromName);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return RegistrationInviteEmailResult.Failed("O remetente configurado no SMTP é inválido.");
        }

        using var message = new MailMessage
        {
            From = origin,
            Subject = CleanHeader($"Seu acesso ao {licenseName} está pronto", 180),
            Body = BuildPlainText(residentName, licenseName, inviteUrl, expiresAt),
            IsBodyHtml = false
        };
        message.To.Add(destination);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            BuildHtmlBody(residentName, licenseName, inviteUrl, expiresAt),
            null,
            MediaTypeNames.Text.Html));

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
            return RegistrationInviteEmailResult.Delivered();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Falha ao enviar convite de cadastro para {Destination}", Mask(recipient));
            return RegistrationInviteEmailResult.Failed(
                "Não foi possível entregar o convite pelo servidor de e-mail.");
        }
    }

    internal static string BuildHtmlBody(
        string residentName,
        string licenseName,
        string inviteUrl,
        DateTime expiresAt)
    {
        var encoder = HtmlEncoder.Default;
        var name = encoder.Encode(string.IsNullOrWhiteSpace(residentName) ? "Morador" : residentName.Trim());
        var condominium = encoder.Encode(licenseName.Trim());
        var url = encoder.Encode(inviteUrl);
        var expiration = expiresAt.ToCondotifyTime().ToString("dd/MM/yyyy 'até' HH:mm");

        return $$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>Convite de acesso F&amp;F Access</title>
            </head>
            <body style="margin:0;padding:0;background:#eef3f9;color:#172238;font-family:Arial,Helvetica,sans-serif">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent">
                Complete seu cadastro e acesse os recursos do seu condomínio com segurança.
              </div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#eef3f9">
                <tr>
                  <td align="center" style="padding:28px 12px">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="max-width:640px;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 12px 36px rgba(9,37,87,.10)">
                      <tr>
                        <td style="padding:34px 38px;background-color:#092557;background-image:linear-gradient(135deg,#092557 0%,#0d4f9f 62%,#087d8b 100%);color:#ffffff">
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                            <tr>
                              <td style="vertical-align:middle">
                                <div style="display:inline-block;padding:9px 13px;border:1px solid rgba(255,255,255,.32);border-radius:10px;font-size:15px;font-weight:800;letter-spacing:1.5px">F&amp;F ACCESS</div>
                              </td>
                              <td align="right" style="vertical-align:middle;font-size:12px;color:#dceaff">CONVITE SEGURO</td>
                            </tr>
                          </table>
                          <h1 style="margin:34px 0 10px;font-size:30px;line-height:1.18;font-weight:800">Seu novo acesso começa aqui.</h1>
                          <p style="margin:0;color:#e6f1ff;font-size:16px;line-height:1.55">{{condominium}} convidou você para fazer parte da experiência F&amp;F Access.</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:36px 38px 14px">
                          <p style="margin:0 0 14px;font-size:18px;line-height:1.5">Olá, <strong>{{name}}</strong>!</p>
                          <p style="margin:0;color:#536176;font-size:15px;line-height:1.7">Seu convite foi preparado. Ao abrir o link, a própria página vai orientar o preenchimento e a criação segura da sua senha.</p>
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="padding:18px 38px 24px">
                          <table role="presentation" cellspacing="0" cellpadding="0" border="0">
                            <tr>
                              <td align="center" bgcolor="#087d68" style="border-radius:12px">
                                <a href="{{url}}" target="_blank" style="display:inline-block;padding:16px 30px;color:#ffffff;text-decoration:none;font-size:16px;font-weight:700;border-radius:12px">Completar meu cadastro&nbsp; →</a>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:14px 0 0;color:#718096;font-size:12px">Convite válido até <strong style="color:#40506a">{{expiration}}</strong></p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:0 38px 28px">
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#f4f8fc;border:1px solid #e2eaf3;border-radius:14px">
                            <tr>
                              <td style="padding:22px 22px 8px;color:#092557;font-size:14px;font-weight:800">Rápido, simples e seguro</td>
                            </tr>
                            <tr>
                              <td style="padding:2px 22px 22px;color:#536176;font-size:13px;line-height:1.75">
                                <strong style="color:#087d68">1.</strong>&nbsp; Acesse pelo botão acima<br>
                                <strong style="color:#087d68">2.</strong>&nbsp; Confira e complete seus dados<br>
                                <strong style="color:#087d68">3.</strong>&nbsp; Crie sua senha e finalize o cadastro
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:0 38px 34px">
                          <p style="margin:0 0 9px;color:#718096;font-size:12px;line-height:1.6">Se o botão não funcionar, copie e cole este endereço no navegador:</p>
                          <p style="margin:0;word-break:break-all;color:#0d4f9f;font-size:12px;line-height:1.6"><a href="{{url}}" style="color:#0d4f9f">{{url}}</a></p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:22px 38px;background:#f8fafc;border-top:1px solid #e7edf4;color:#7b8799;font-size:11px;line-height:1.6">
                          Este convite é pessoal. Não encaminhe o link. A F&amp;F Access nunca solicitará sua senha por e-mail.<br>
                          Se você não esperava este convite, ignore esta mensagem com segurança.
                        </td>
                      </tr>
                    </table>
                    <p style="margin:18px 0 0;color:#8995a7;font-size:11px">F&amp;F Access · Tecnologia para uma rotina mais segura</p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    internal static string BuildPlainText(
        string residentName,
        string licenseName,
        string inviteUrl,
        DateTime expiresAt) =>
        $"""
        Olá, {(string.IsNullOrWhiteSpace(residentName) ? "Morador" : residentName.Trim())}!

        {licenseName.Trim()} convidou você para completar seu cadastro no F&F Access.
        A página vai orientar o preenchimento dos dados e a criação da sua senha.

        Acesse: {inviteUrl}
        Convite válido até {expiresAt.ToCondotifyTime():dd/MM/yyyy 'até' HH:mm}.

        Este convite é pessoal. Não encaminhe o link e nunca informe sua senha por e-mail.
        Se você não esperava este convite, ignore esta mensagem.

        F&F Access
        """;

    private SmtpSettings? Resolve(AlertNotificationPolicyDTO? policy)
    {
        if (policy is not null && !string.IsNullOrWhiteSpace(policy.SmtpHost) &&
            !string.IsNullOrWhiteSpace(policy.SmtpFromEmail) && policy.SmtpPort is > 0 and <= 65535)
            return new SmtpSettings(
                policy.SmtpHost.Trim(),
                policy.SmtpPort,
                policy.SmtpUsername,
                policy.SmtpPassword,
                policy.SmtpFromEmail,
                string.IsNullOrWhiteSpace(policy.SmtpFromName) ? "F&F Access" : policy.SmtpFromName,
                policy.SmtpEnableSsl);

        var host = Get("CONDOTIFY_SMTP_HOST", "Notifications:Smtp:Host");
        var from = Get("CONDOTIFY_SMTP_FROM_EMAIL", "Notifications:Smtp:FromEmail");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return null;
        var port = int.TryParse(Get("CONDOTIFY_SMTP_PORT", "Notifications:Smtp:Port"), out var parsedPort)
            ? parsedPort
            : 587;
        var enableSsl = !bool.TryParse(
            Get("CONDOTIFY_SMTP_ENABLE_SSL", "Notifications:Smtp:EnableSsl"),
            out var parsedSsl) || parsedSsl;
        return new SmtpSettings(
            host,
            port,
            Get("CONDOTIFY_SMTP_USERNAME", "Notifications:Smtp:Username") ?? string.Empty,
            Get("CONDOTIFY_SMTP_PASSWORD", "Notifications:Smtp:Password") ?? string.Empty,
            from,
            Get("CONDOTIFY_SMTP_FROM_NAME", "Notifications:Smtp:FromName") ?? "F&F Access",
            enableSsl);
    }

    private string? Get(string environmentName, string configurationName) =>
        Environment.GetEnvironmentVariable(environmentName) ?? configuration[configurationName];

    internal static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "e-mail protegido";
        return $"{email[0]}***@{email[(at + 1)..]}";
    }

    internal static string CleanHeader(string value, int maxLength)
    {
        var cleaned = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private sealed record SmtpSettings(
        string Host,
        int Port,
        string Username,
        string Password,
        string FromEmail,
        string FromName,
        bool EnableSsl);
}
