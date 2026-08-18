using System.Security.Claims;
using System.Net.Mail;
using System.Text.Json;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/notifications")]
[RequireLicensePermission(LicensePermissionEnum.ViewAlerts)]
public sealed class AlertNotificationsController(
    DatabaseContext context,
    IAlertNotificationChannelSender sender,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("policy")]
    public async Task<IActionResult> GetPolicy(Guid licenseId)
    {
        var policy = await context.AlertNotificationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        return Ok(ToOut(policy, sender));
    }

    [HttpPut("policy")]
    [RequireLicensePermission(LicensePermissionEnum.ManageSettings)]
    public async Task<IActionResult> UpdatePolicy(
        Guid licenseId,
        [FromBody] UpdateAlertNotificationPolicyIn input)
    {
        if (!Enum.TryParse<OperationalAlertSeverity>(input.MinimumSeverity, true, out var minimumSeverity))
            return BadRequest(new { Result = "InvalidSeverity", Errors = "Selecione uma severidade mínima válida." });
        if (input.WarningSlaMinutes is < 5 or > 10080 ||
            input.CriticalSlaMinutes is < 5 or > 10080)
            return BadRequest(new { Result = "InvalidSla", Errors = "O SLA deve ficar entre 5 minutos e 7 dias." });
        if (input.EscalationRepeatMinutes is < 15 or > 10080)
            return BadRequest(new { Result = "InvalidEscalation", Errors = "A repetição deve ficar entre 15 minutos e 7 dias." });

        var policy = await context.AlertNotificationPolicies
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (policy is null)
        {
            policy = new AlertNotificationPolicyDTO { LicenseId = licenseId };
            context.AlertNotificationPolicies.Add(policy);
        }

        if (input.ClearWebhook)
        {
            policy.WebhookUrl = string.Empty;
            policy.WebhookSecret = string.Empty;
            policy.WebhookEnabled = false;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(input.WebhookUrl))
            {
                if (!ValidWebhook(input.WebhookUrl, environment.IsDevelopment()))
                    return BadRequest(new
                    {
                        Result = "InvalidWebhook",
                        Errors = environment.IsDevelopment()
                            ? "Informe um endpoint HTTP ou HTTPS válido, sem credenciais na URL."
                            : "O webhook deve usar HTTPS e não pode conter credenciais na URL."
                    });
                policy.WebhookUrl = input.WebhookUrl.Trim();
            }
            if (!string.IsNullOrWhiteSpace(input.WebhookSecret))
                policy.WebhookSecret = input.WebhookSecret.Trim();
            policy.WebhookEnabled = input.WebhookEnabled;
        }

        var recipients = AlertNotificationChannelSender.ParseRecipients(input.EmailRecipients);
        if (input.EmailEnabled && recipients.Count == 0)
            return BadRequest(new { Result = "InvalidRecipients", Errors = "Informe ao menos um destinatário de e-mail válido." });
        if (input.EmailEnabled && !sender.IsEmailTransportReady(policy))
            return BadRequest(new
            {
                Result = "SmtpUnavailable",
                Errors = "Configure o servidor SMTP em Administração > Notificações antes de habilitar e-mails."
            });
        if (input.WebhookEnabled && string.IsNullOrWhiteSpace(policy.WebhookUrl))
            return BadRequest(new { Result = "WebhookMissing", Errors = "Configure o endpoint antes de habilitar o webhook." });

        policy.Enabled = input.Enabled;
        policy.MinimumSeverity = minimumSeverity;
        policy.WarningSlaMinutes = input.WarningSlaMinutes;
        policy.CriticalSlaMinutes = input.CriticalSlaMinutes;
        policy.EscalationRepeatMinutes = input.EscalationRepeatMinutes;
        policy.EmailEnabled = input.EmailEnabled;
        policy.EmailRecipients = string.Join(';', recipients.Select(x => x.Address).Distinct(StringComparer.OrdinalIgnoreCase));
        policy.UpdatedAt = DateTime.UtcNow;
        AddAudit(licenseId, "NotificationPolicyUpdated", "Política de notificações atualizada.", new
        {
            policy.Enabled,
            policy.MinimumSeverity,
            policy.WarningSlaMinutes,
            policy.CriticalSlaMinutes,
            policy.EscalationRepeatMinutes,
            policy.WebhookEnabled,
            policy.EmailEnabled,
            RecipientCount = recipients.Count
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToOut(policy, sender));
    }

    [HttpGet("smtp")]
    [RequireLicensePermission(LicensePermissionEnum.ViewSettings)]
    public async Task<IActionResult> GetSmtp(Guid licenseId)
    {
        var policy = await context.AlertNotificationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted)
            ?? new AlertNotificationPolicyDTO { LicenseId = licenseId };
        return Ok(ToSmtpOut(policy, sender));
    }

    [HttpPut("smtp")]
    [RequireLicensePermission(LicensePermissionEnum.ManageSettings)]
    public async Task<IActionResult> UpdateSmtp(Guid licenseId, [FromBody] UpdateSmtpSettingsIn input)
    {
        var host = input.Host?.Trim() ?? string.Empty;
        var username = input.Username?.Trim() ?? string.Empty;
        var password = input.Password ?? string.Empty;
        var fromEmail = input.FromEmail?.Trim() ?? string.Empty;
        var fromName = string.IsNullOrWhiteSpace(input.FromName) ? "F&F Access" : input.FromName.Trim();

        if (!ValidSmtpHost(host))
            return BadRequest(new { Result = "InvalidSmtpHost", Errors = "Informe um servidor SMTP válido, sem protocolo ou caminho." });
        if (input.Port is < 1 or > 65535)
            return BadRequest(new { Result = "InvalidSmtpPort", Errors = "A porta SMTP deve ficar entre 1 e 65535." });
        if (!ValidEmail(fromEmail))
            return BadRequest(new { Result = "InvalidFromEmail", Errors = "Informe um e-mail de remetente válido." });
        if (host.Length > 200 ||
            username.Length > 300 ||
            password.Length > 1000 ||
            fromEmail.Length > 300 ||
            fromName.Length > 120 ||
            HasControlCharacters(username) ||
            HasControlCharacters(fromName))
            return BadRequest(new { Result = "InvalidSmtpSettings", Errors = "Um dos campos é inválido ou excede o tamanho permitido." });

        var policy = await context.AlertNotificationPolicies
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (policy is null)
        {
            policy = new AlertNotificationPolicyDTO { LicenseId = licenseId };
            context.AlertNotificationPolicies.Add(policy);
        }
        if (!string.IsNullOrWhiteSpace(username) &&
            string.IsNullOrWhiteSpace(password) &&
            string.IsNullOrWhiteSpace(policy.SmtpPassword))
            return BadRequest(new
            {
                Result = "SmtpPasswordRequired",
                Errors = "Informe a senha SMTP ao configurar um usuário autenticado."
            });

        policy.SmtpHost = host;
        policy.SmtpPort = input.Port;
        policy.SmtpUsername = username;
        if (!string.IsNullOrWhiteSpace(password))
            policy.SmtpPassword = password;
        policy.SmtpFromEmail = fromEmail;
        policy.SmtpFromName = fromName;
        policy.SmtpEnableSsl = input.EnableSsl;
        policy.UpdatedAt = DateTime.UtcNow;
        AddAudit(licenseId, "SmtpSettingsUpdated", "Configuração SMTP atualizada pela administração web.", new
        {
            policy.SmtpHost,
            policy.SmtpPort,
            HasUsername = !string.IsNullOrWhiteSpace(policy.SmtpUsername),
            HasFromEmail = !string.IsNullOrWhiteSpace(policy.SmtpFromEmail),
            policy.SmtpEnableSsl
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToSmtpOut(policy, sender));
    }

    [HttpDelete("smtp")]
    [RequireLicensePermission(LicensePermissionEnum.ManageSettings)]
    public async Task<IActionResult> DeleteSmtp(Guid licenseId)
    {
        var policy = await context.AlertNotificationPolicies
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (policy is null) return NoContent();
        policy.SmtpHost = string.Empty;
        policy.SmtpPort = 587;
        policy.SmtpUsername = string.Empty;
        policy.SmtpPassword = string.Empty;
        policy.SmtpFromEmail = string.Empty;
        policy.SmtpFromName = "F&F Access";
        policy.SmtpEnableSsl = true;
        policy.UpdatedAt = DateTime.UtcNow;
        AddAudit(licenseId, "SmtpSettingsRemoved", "Configuração SMTP web removida.", new { });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToSmtpOut(policy, sender));
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> GetDeliveries(Guid licenseId, [FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 10, 200);
        var deliveries = await context.AlertNotificationDeliveries.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .Select(x => new AlertNotificationDeliveryOut
            {
                Id = x.Id,
                AlertId = x.AlertId,
                AlertTitle = x.Alert.Title,
                Channel = x.Channel.ToString(),
                Status = x.Status.ToString(),
                EscalationLevel = x.EscalationLevel,
                DestinationLabel = x.DestinationLabel,
                AttemptCount = x.AttemptCount,
                ResponseCode = x.ResponseCode,
                LastError = x.LastError,
                CreatedAt = x.CreatedAt,
                SentAt = x.SentAt,
                FinishedAt = x.FinishedAt
            })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(deliveries);
    }

    [HttpPost("test")]
    [RequireLicensePermission(LicensePermissionEnum.ManageSettings)]
    public async Task<IActionResult> TestChannel(
        Guid licenseId,
        [FromBody] TestAlertNotificationChannelIn input)
    {
        if (!Enum.TryParse<NotificationChannel>(input.Channel, true, out var channel))
            return BadRequest(new { Result = "InvalidChannel", Errors = "Canal de notificação inválido." });
        var policy = await context.AlertNotificationPolicies.AsNoTracking()
            .Include(x => x.License)
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (policy is null)
            return Conflict(new { Result = "PolicyMissing", Errors = "Salve a política antes de testar os canais." });
        if (channel == NotificationChannel.Webhook && string.IsNullOrWhiteSpace(policy.WebhookUrl))
            return Conflict(new { Result = "WebhookMissing", Errors = "O webhook ainda não foi configurado." });
        if (channel == NotificationChannel.Email &&
            (!sender.IsEmailTransportReady(policy) || AlertNotificationChannelSender.ParseRecipients(policy.EmailRecipients).Count == 0))
            return Conflict(new { Result = "EmailUnavailable", Errors = "O canal de e-mail ainda não está pronto." });

        var result = await sender.SendAsync(channel, policy, new AlertNotificationMessage(
            Guid.NewGuid(),
            licenseId,
            policy.License.Name,
            "ChannelTest",
            OperationalAlertSeverity.Info.ToString(),
            "Teste de notificação da F&F Access",
            "O canal foi acionado manualmente pela central de administração.",
            "Test",
            0,
            DateTime.UtcNow,
            $"/licencas/{licenseId}/administracao"), HttpContext.RequestAborted);
        AddAudit(
            licenseId,
            "NotificationChannelTested",
            result.Success ? $"Teste de {channel} concluído." : $"Teste de {channel} falhou.",
            new { Channel = channel.ToString(), result.Success, result.ResponseCode, result.Error });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return result.Success
            ? Ok(new TestAlertNotificationChannelOut { Success = true, Message = "Notificação de teste enviada." })
            : StatusCode(StatusCodes.Status502BadGateway, new TestAlertNotificationChannelOut
            {
                Success = false,
                Message = result.Error
            });
    }

    private void AddAudit(Guid licenseId, string action, string summary, object details)
    {
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "AlertNotification",
            Action = action,
            Status = action.EndsWith("Tested", StringComparison.Ordinal) ? "Recorded" : "Success",
            Summary = summary,
            DetailsJson = JsonSerializer.Serialize(details),
            UserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null,
            UserName = User.Identity?.Name ?? "Usuário",
            CreatedAt = DateTime.UtcNow
        });
    }

    private static AlertNotificationPolicyOut ToOut(
        AlertNotificationPolicyDTO? policy,
        IAlertNotificationChannelSender channelSender) => new()
    {
        Enabled = policy?.Enabled ?? false,
        MinimumSeverity = (policy?.MinimumSeverity ?? OperationalAlertSeverity.Warning).ToString(),
        WarningSlaMinutes = policy?.WarningSlaMinutes ?? 60,
        CriticalSlaMinutes = policy?.CriticalSlaMinutes ?? 15,
        EscalationRepeatMinutes = policy?.EscalationRepeatMinutes ?? 60,
        WebhookEnabled = policy?.WebhookEnabled ?? false,
        WebhookConfigured = !string.IsNullOrWhiteSpace(policy?.WebhookUrl),
        WebhookEndpointLabel = string.IsNullOrWhiteSpace(policy?.WebhookUrl)
            ? string.Empty
            : AlertNotificationWorker.EndpointLabel(policy.WebhookUrl),
        EmailEnabled = policy?.EmailEnabled ?? false,
        EmailRecipients = policy?.EmailRecipients ?? string.Empty,
        EmailTransportReady = channelSender.IsEmailTransportReady(policy ?? new AlertNotificationPolicyDTO()),
        UpdatedAt = policy?.UpdatedAt ?? DateTime.MinValue
    };

    private static SmtpSettingsOut ToSmtpOut(
        AlertNotificationPolicyDTO policy,
        IAlertNotificationChannelSender channelSender)
    {
        var webConfigured = !string.IsNullOrWhiteSpace(policy.SmtpHost) &&
                            !string.IsNullOrWhiteSpace(policy.SmtpFromEmail);
        return new SmtpSettingsOut
        {
            Configured = channelSender.IsEmailTransportReady(policy),
            Source = channelSender.EmailTransportSource(policy),
            Host = webConfigured ? policy.SmtpHost : string.Empty,
            Port = webConfigured ? policy.SmtpPort : 587,
            Username = webConfigured ? policy.SmtpUsername : string.Empty,
            PasswordConfigured = webConfigured && !string.IsNullOrWhiteSpace(policy.SmtpPassword),
            FromEmail = webConfigured ? policy.SmtpFromEmail : string.Empty,
            FromName = webConfigured ? policy.SmtpFromName : "F&F Access",
            EnableSsl = !webConfigured || policy.SmtpEnableSsl
        };
    }

    private static bool ValidWebhook(string value, bool development)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo))
            return false;
        return uri.Scheme == Uri.UriSchemeHttps ||
               (development && uri.Scheme == Uri.UriSchemeHttp);
    }

    private static bool ValidSmtpHost(string value)
    {
        var host = value.Trim();
        return host.Length > 0 &&
               host.Length <= 200 &&
               !host.Contains("://", StringComparison.Ordinal) &&
               !host.Contains('/') &&
               !host.Contains('\\') &&
               Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    private static bool ValidEmail(string value)
    {
        try { return new MailAddress(value.Trim()).Address == value.Trim(); }
        catch (FormatException) { return false; }
    }

    private static bool HasControlCharacters(string value) =>
        value.Any(char.IsControl);
}
