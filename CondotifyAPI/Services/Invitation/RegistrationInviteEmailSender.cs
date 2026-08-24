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

    var name = encoder.Encode(
        string.IsNullOrWhiteSpace(residentName)
            ? "Morador"
            : residentName.Trim());

    var condominium = encoder.Encode(licenseName.Trim());
    var url = encoder.Encode(inviteUrl);
    var expiration = expiresAt
        .ToCondotifyTime()
        .ToString("dd/MM/yyyy 'às' HH:mm");

    return $$"""
        <!doctype html>
        <html lang="pt-BR">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="x-apple-disable-message-reformatting">
          <meta name="format-detection" content="telephone=no,date=no,address=no,email=no">

          <title>Seu acesso ao F&amp;F Access</title>

          <style>
            @media only screen and (max-width: 640px) {
              .email-container {
                width: 100% !important;
              }

              .content-padding {
                padding-left: 24px !important;
                padding-right: 24px !important;
              }

              .header-padding {
                padding: 28px 24px !important;
              }

              .title {
                font-size: 27px !important;
                line-height: 34px !important;
              }

              .button-link {
                display: block !important;
                width: auto !important;
              }

              .step-number {
                width: 42px !important;
              }

              .footer-padding {
                padding-left: 24px !important;
                padding-right: 24px !important;
              }
            }
          </style>
        </head>

        <body
          style="
            margin:0;
            padding:0;
            width:100%;
            background-color:#eef3f8;
            color:#172238;
            font-family:Arial,Helvetica,sans-serif;
            -webkit-font-smoothing:antialiased;
            -moz-osx-font-smoothing:grayscale;
          ">

          <!-- Preheader -->
          <div
            style="
              display:none;
              max-height:0;
              max-width:0;
              overflow:hidden;
              opacity:0;
              color:transparent;
              mso-hide:all;
            ">
            Seu convite para acessar o F&amp;F Access está pronto. Ative sua conta com segurança.
          </div>

          <table
            role="presentation"
            width="100%"
            cellspacing="0"
            cellpadding="0"
            border="0"
            style="width:100%;background-color:#eef3f8;">

            <tr>
              <td align="center" style="padding:36px 14px;">

                <!-- Container principal -->
                <table
                  role="presentation"
                  width="640"
                  cellspacing="0"
                  cellpadding="0"
                  border="0"
                  class="email-container"
                  style="
                    width:640px;
                    max-width:640px;
                    background-color:#ffffff;
                    border-radius:18px;
                    overflow:hidden;
                    box-shadow:0 12px 34px rgba(9,37,87,.08);
                  ">

                  <!-- HEADER -->
                  <tr>
                    <td
                      class="header-padding"
                      style="
                        padding:34px 40px 38px;
                        background-color:#092557;
                        background-image:linear-gradient(
                          135deg,
                          #092557 0%,
                          #0d4f9f 58%,
                          #087d8b 100%
                        );
                      ">

                      <table
                        role="presentation"
                        width="100%"
                        cellspacing="0"
                        cellpadding="0"
                        border="0">

                        <tr>
                          <td
                            align="left"
                            valign="middle"
                            style="padding:0;">

                            <div
                              style="
                                display:inline-block;
                                padding:9px 14px;
                                border:1px solid rgba(255,255,255,.34);
                                border-radius:9px;
                                color:#ffffff;
                                font-size:14px;
                                font-weight:800;
                                letter-spacing:1.7px;
                              ">
                              F&amp;F ACCESS
                            </div>

                          </td>

                          <td
                            align="right"
                            valign="middle"
                            style="
                              padding:0;
                              color:#dbeafe;
                              font-size:11px;
                              font-weight:700;
                              letter-spacing:.8px;
                            ">
                            CONVITE DE ACESSO
                          </td>
                        </tr>

                      </table>

                      <h1
                        class="title"
                        style="
                          margin:34px 0 10px;
                          color:#ffffff;
                          font-size:32px;
                          line-height:39px;
                          font-weight:800;
                          letter-spacing:-.4px;
                        ">
                        Seu acesso está quase pronto.
                      </h1>

                      <p
                        style="
                          margin:0;
                          color:#e5efff;
                          font-size:16px;
                          line-height:25px;
                        ">
                        <strong style="color:#ffffff;">{{condominium}}</strong>
                        convidou você para utilizar o F&amp;F Access.
                      </p>

                    </td>
                  </tr>

                  <!-- SAUDAÇÃO -->
                  <tr>
                    <td
                      class="content-padding"
                      style="padding:38px 40px 0;">

                      <p
                        style="
                          margin:0 0 12px;
                          color:#172238;
                          font-size:19px;
                          line-height:29px;
                        ">
                        Olá, <strong>{{name}}</strong>!
                      </p>

                      <p
                        style="
                          margin:0;
                          color:#536176;
                          font-size:15px;
                          line-height:25px;
                        ">
                        Falta apenas um passo para ativar seu acesso.
                        Confirme seus dados e crie sua senha para começar
                        a utilizar o F&amp;F Access.
                      </p>

                    </td>
                  </tr>

                  <!-- BOTÃO -->
                  <tr>
                    <td
                      align="center"
                      class="content-padding"
                      style="padding:30px 40px 12px;">

                      <table
                        role="presentation"
                        cellspacing="0"
                        cellpadding="0"
                        border="0">

                        <tr>
                          <td
                            align="center"
                            bgcolor="#087d68"
                            style="
                              border-radius:12px;
                              box-shadow:0 6px 14px rgba(8,125,104,.18);
                            ">

                            <a
                              href="{{url}}"
                              target="_blank"
                              class="button-link"
                              style="
                                display:inline-block;
                                padding:16px 32px;
                                color:#ffffff;
                                text-decoration:none;
                                font-size:16px;
                                line-height:20px;
                                font-weight:700;
                                border-radius:12px;
                              ">
                              Ativar meu acesso&nbsp;&nbsp;→
                            </a>

                          </td>
                        </tr>

                      </table>

                    </td>
                  </tr>

                  <!-- EXPIRAÇÃO -->
                  <tr>
                    <td
                      align="center"
                      class="content-padding"
                      style="padding:6px 40px 32px;">

                      <table
                        role="presentation"
                        cellspacing="0"
                        cellpadding="0"
                        border="0">

                        <tr>
                          <td
                            style="
                              padding:8px 14px;
                              background-color:#f4f7fb;
                              border:1px solid #e3eaf2;
                              border-radius:20px;
                              color:#69778c;
                              font-size:12px;
                              line-height:18px;
                            ">
                            Convite válido até
                            <strong style="color:#33445e;">
                              {{expiration}}
                            </strong>
                          </td>
                        </tr>

                      </table>

                    </td>
                  </tr>

                  <!-- DIVISOR -->
                  <tr>
                    <td
                      class="content-padding"
                      style="padding:0 40px;">
                      <div
                        style="
                          height:1px;
                          background-color:#edf1f6;
                          line-height:1px;
                          font-size:1px;
                        ">
                        &nbsp;
                      </div>
                    </td>
                  </tr>

                  <!-- PASSO A PASSO -->
                  <tr>
                    <td
                      class="content-padding"
                      style="padding:30px 40px 10px;">

                      <p
                        style="
                          margin:0 0 22px;
                          color:#092557;
                          font-size:17px;
                          line-height:24px;
                          font-weight:800;
                        ">
                        Como concluir seu cadastro
                      </p>

                      <!-- PASSO 1 -->
                      <table
                        role="presentation"
                        width="100%"
                        cellspacing="0"
                        cellpadding="0"
                        border="0"
                        style="margin-bottom:20px;">

                        <tr>
                          <td
                            valign="top"
                            width="52"
                            class="step-number">

                            <div
                              style="
                                width:38px;
                                height:38px;
                                line-height:38px;
                                text-align:center;
                                border-radius:50%;
                                background-color:#e7f6f2;
                                color:#087d68;
                                font-size:13px;
                                font-weight:800;
                              ">
                              01
                            </div>

                          </td>

                          <td valign="top">

                            <p
                              style="
                                margin:1px 0 4px;
                                color:#172238;
                                font-size:14px;
                                line-height:21px;
                                font-weight:700;
                              ">
                              Acesse seu convite
                            </p>

                            <p
                              style="
                                margin:0;
                                color:#66758a;
                                font-size:13px;
                                line-height:20px;
                              ">
                              Clique no botão acima para abrir
                              a página segura de ativação.
                            </p>

                          </td>
                        </tr>

                      </table>

                      <!-- PASSO 2 -->
                      <table
                        role="presentation"
                        width="100%"
                        cellspacing="0"
                        cellpadding="0"
                        border="0"
                        style="margin-bottom:20px;">

                        <tr>
                          <td
                            valign="top"
                            width="52"
                            class="step-number">

                            <div
                              style="
                                width:38px;
                                height:38px;
                                line-height:38px;
                                text-align:center;
                                border-radius:50%;
                                background-color:#e7f6f2;
                                color:#087d68;
                                font-size:13px;
                                font-weight:800;
                              ">
                              02
                            </div>

                          </td>

                          <td valign="top">

                            <p
                              style="
                                margin:1px 0 4px;
                                color:#172238;
                                font-size:14px;
                                line-height:21px;
                                font-weight:700;
                              ">
                              Confirme seus dados
                            </p>

                            <p
                              style="
                                margin:0;
                                color:#66758a;
                                font-size:13px;
                                line-height:20px;
                              ">
                              Revise as informações apresentadas
                              e complete o que for necessário.
                            </p>

                          </td>
                        </tr>

                      </table>

                      <!-- PASSO 3 -->
                      <table
                        role="presentation"
                        width="100%"
                        cellspacing="0"
                        cellpadding="0"
                        border="0">

                        <tr>
                          <td
                            valign="top"
                            width="52"
                            class="step-number">

                            <div
                              style="
                                width:38px;
                                height:38px;
                                line-height:38px;
                                text-align:center;
                                border-radius:50%;
                                background-color:#e7f6f2;
                                color:#087d68;
                                font-size:13px;
                                font-weight:800;
                              ">
                              03
                            </div>

                          </td>

                          <td valign="top">

                            <p
                              style="
                                margin:1px 0 4px;
                                color:#172238;
                                font-size:14px;
                                line-height:21px;
                                font-weight:700;
                              ">
                              Crie sua senha
                            </p>

                            <p
                              style="
                                margin:0;
                                color:#66758a;
                                font-size:13px;
                                line-height:20px;
                              ">
                              Defina uma senha segura e finalize
                              a ativação da sua conta.
                            </p>

                          </td>
                        </tr>

                      </table>

                    </td>
                  </tr>

                  <!-- SEGURANÇA -->
                  <tr>
                    <td
                      class="content-padding"
                      style="padding:28px 40px 0;">

                      <table
                        role="presentation"
                        width="100%"
                        cellspacing="0"
                        cellpadding="0"
                        border="0"
                        style="
                          background-color:#f2f7fc;
                          border:1px solid #dce8f4;
                          border-radius:14px;
                        ">

                        <tr>
                          <td style="padding:20px 22px;">

                            <table
                              role="presentation"
                              width="100%"
                              cellspacing="0"
                              cellpadding="0"
                              border="0">

                              <tr>
                                <td
                                  valign="top"
                                  width="34"
                                  style="
                                    color:#0d4f9f;
                                    font-size:21px;
                                    line-height:24px;
                                  ">
                                  ✓
                                </td>

                                <td valign="top">

                                  <p
                                    style="
                                      margin:0 0 6px;
                                      color:#092557;
                                      font-size:14px;
                                      line-height:20px;
                                      font-weight:800;
                                    ">
                                    Sua segurança é importante
                                  </p>

                                  <p
                                    style="
                                      margin:0;
                                      color:#5f6f84;
                                      font-size:12px;
                                      line-height:20px;
                                    ">
                                    Este convite é pessoal e intransferível.
                                    Não compartilhe este link.
                                    A F&amp;F Access nunca solicitará
                                    sua senha por e-mail.
                                  </p>

                                </td>
                              </tr>

                            </table>

                          </td>
                        </tr>

                      </table>

                    </td>
                  </tr>

                  <!-- LINK ALTERNATIVO -->
                  <tr>
                    <td
                      class="content-padding"
                      style="padding:28px 40px 34px;">

                      <p
                        style="
                          margin:0 0 8px;
                          color:#7a8798;
                          font-size:11px;
                          line-height:18px;
                        ">
                        Se o botão não funcionar, copie e cole
                        o endereço abaixo no seu navegador:
                      </p>

                      <div
                        style="
                          padding:12px 14px;
                          background-color:#f8fafc;
                          border:1px solid #e7edf4;
                          border-radius:8px;
                          word-break:break-all;
                        ">

                        <a
                          href="{{url}}"
                          target="_blank"
                          style="
                            color:#0d4f9f;
                            text-decoration:none;
                            font-size:11px;
                            line-height:18px;
                          ">
                          {{url}}
                        </a>

                      </div>

                    </td>
                  </tr>

                  <!-- RODAPÉ INTERNO -->
                  <tr>
                    <td
                      class="footer-padding"
                      style="
                        padding:22px 40px;
                        background-color:#f8fafc;
                        border-top:1px solid #e7edf4;
                      ">

                      <p
                        style="
                          margin:0;
                          color:#7c899b;
                          font-size:11px;
                          line-height:18px;
                        ">
                        Se você não reconhece ou não esperava este convite,
                        nenhuma ação é necessária. Basta ignorar esta mensagem.
                      </p>

                    </td>
                  </tr>

                </table>

                <!-- FOOTER EXTERNO -->
                <table
                  role="presentation"
                  width="640"
                  cellspacing="0"
                  cellpadding="0"
                  border="0"
                  class="email-container"
                  style="
                    width:640px;
                    max-width:640px;
                  ">

                  <tr>
                    <td
                      align="center"
                      style="padding:20px 20px 4px;">

                      <p
                        style="
                          margin:0 0 5px;
                          color:#7f8da0;
                          font-size:11px;
                          line-height:18px;
                          font-weight:700;
                        ">
                        F&amp;F Access
                      </p>

                      <p
                        style="
                          margin:0;
                          color:#9aa5b5;
                          font-size:10px;
                          line-height:17px;
                        ">
                        Tecnologia para tornar o acesso mais simples e seguro.
                      </p>

                    </td>
                  </tr>

                </table>

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
