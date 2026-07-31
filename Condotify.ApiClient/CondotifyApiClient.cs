using Condotify.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Condotify.Services;

public sealed class CondotifyApiClient
{
    // Mirrors the API's global JsonStringEnumConverter (CondotifyAPI/Program.cs) so enum
    // properties serialized as strings (e.g. "Saturday") deserialize correctly on this side.
    private static readonly JsonSerializerOptions GetJsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionContextProvider _sessionContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CondotifyApiClient> _logger;

    public CondotifyApiClient(
        IHttpClientFactory httpClientFactory,
        ISessionContextProvider sessionContext,
        IConfiguration configuration,
        ILogger<CondotifyApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionContext = sessionContext;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<ApiResult<List<LicenseViewModel>>> GetLicensesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<LicenseViewModel>>("api/access/licenses", cancellationToken);

    public Task<ApiResult<OperationalDashboardViewModel>> GetOperationalDashboardAsync(CancellationToken cancellationToken = default) =>
        GetAsync<OperationalDashboardViewModel>("api/access/operations/dashboard", cancellationToken);

    public Task<ApiResult<OperationalAlertPageViewModel>> GetOperationalAlertsAsync(
        string status,
        string severity,
        CancellationToken cancellationToken = default)
    {
        var query = $"status={Uri.EscapeDataString(status)}&severity={Uri.EscapeDataString(severity)}&pageSize=100";
        return GetAsync<OperationalAlertPageViewModel>($"api/access/operations/alerts?{query}", cancellationToken);
    }

    public Task<ApiResult<OperationalAlertSummaryViewModel>> GetOperationalAlertSummaryAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<OperationalAlertSummaryViewModel>("api/access/operations/alerts/summary", cancellationToken);

    public Task<ApiResult<OperationalAlertViewModel>> AcknowledgeOperationalAlertAsync(
        Guid alertId,
        string note = "",
        CancellationToken cancellationToken = default) =>
        SendForAsync<OperationalAlertViewModel>(
            HttpMethod.Post,
            $"api/access/operations/alerts/{alertId}/acknowledge",
            new { Note = note },
            cancellationToken);

    public Task<ApiResult<OperationalAlertViewModel>> ResolveOperationalAlertAsync(
        Guid alertId,
        string note = "",
        CancellationToken cancellationToken = default) =>
        SendForAsync<OperationalAlertViewModel>(
            HttpMethod.Post,
            $"api/access/operations/alerts/{alertId}/resolve",
            new { Note = note },
            cancellationToken);

    public Task<ApiResult<OperationalAlertViewModel>> ReopenOperationalAlertAsync(
        Guid alertId,
        string note = "",
        CancellationToken cancellationToken = default) =>
        SendForAsync<OperationalAlertViewModel>(
            HttpMethod.Post,
            $"api/access/operations/alerts/{alertId}/reopen",
            new { Note = note },
            cancellationToken);

    public Task<ApiResult<OperationalAlertViewModel>> SnoozeOperationalAlertAsync(
        Guid alertId,
        int minutes,
        string note = "",
        CancellationToken cancellationToken = default) =>
        SendForAsync<OperationalAlertViewModel>(
            HttpMethod.Post,
            $"api/access/operations/alerts/{alertId}/snooze",
            new { Minutes = minutes, Note = note },
            cancellationToken);

    public Task<ApiResult<OperationalAlertViewModel>> UnsnoozeOperationalAlertAsync(
        Guid alertId,
        CancellationToken cancellationToken = default) =>
        SendForAsync<OperationalAlertViewModel>(
            HttpMethod.Post,
            $"api/access/operations/alerts/{alertId}/unsnooze",
            new { Note = string.Empty },
            cancellationToken);

    public Task<ApiResult<AlertNotificationPolicyViewModel>> GetAlertNotificationPolicyAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AlertNotificationPolicyViewModel>(
            $"api/access/licenses/{licenseId}/notifications/policy",
            cancellationToken);

    public Task<ApiResult<AlertNotificationPolicyViewModel>> UpdateAlertNotificationPolicyAsync(
        Guid licenseId,
        AlertNotificationPolicyViewModel model,
        CancellationToken cancellationToken = default) =>
        SendForAsync<AlertNotificationPolicyViewModel>(
            HttpMethod.Put,
            $"api/access/licenses/{licenseId}/notifications/policy",
            new
            {
                model.Enabled,
                model.MinimumSeverity,
                model.WarningSlaMinutes,
                model.CriticalSlaMinutes,
                model.EscalationRepeatMinutes,
                model.WebhookEnabled,
                model.WebhookUrl,
                model.WebhookSecret,
                model.ClearWebhook,
                model.EmailEnabled,
                model.EmailRecipients
            },
            cancellationToken);

    public Task<ApiResult<List<AlertNotificationDeliveryViewModel>>> GetAlertNotificationDeliveriesAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<List<AlertNotificationDeliveryViewModel>>(
            $"api/access/licenses/{licenseId}/notifications/deliveries?take=100",
            cancellationToken);

    public Task<ApiResult<AlertNotificationTestViewModel>> TestAlertNotificationChannelAsync(
        Guid licenseId,
        string channel,
        CancellationToken cancellationToken = default) =>
        SendForAsync<AlertNotificationTestViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/notifications/test",
            new { Channel = channel },
            cancellationToken);

    public Task<ApiResult<SmtpSettingsViewModel>> GetSmtpSettingsAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<SmtpSettingsViewModel>(
            $"api/access/licenses/{licenseId}/notifications/smtp",
            cancellationToken);

    public Task<ApiResult<SmtpSettingsViewModel>> UpdateSmtpSettingsAsync(
        Guid licenseId,
        SmtpSettingsViewModel model,
        CancellationToken cancellationToken = default) =>
        SendForAsync<SmtpSettingsViewModel>(
            HttpMethod.Put,
            $"api/access/licenses/{licenseId}/notifications/smtp",
            new
            {
                model.Host,
                model.Port,
                model.Username,
                model.Password,
                model.FromEmail,
                model.FromName,
                model.EnableSsl
            },
            cancellationToken);

    public Task<ApiResult<SmtpSettingsViewModel>> DeleteSmtpSettingsAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        SendForAsync<SmtpSettingsViewModel>(
            HttpMethod.Delete,
            $"api/access/licenses/{licenseId}/notifications/smtp",
            new { },
            cancellationToken);

    public Task<ApiResult<MfaSecurityViewModel>> GetMfaStatusAsync(CancellationToken cancellationToken = default) =>
        GetAsync<MfaSecurityViewModel>("api/auth/mfa/status", cancellationToken);

    public Task<ApiResult<MfaSecurityViewModel>> SetupMfaAsync(CancellationToken cancellationToken = default) =>
        SendForAsync<MfaSecurityViewModel>(HttpMethod.Post, "api/auth/mfa/setup", new { }, cancellationToken);

    public Task<ApiResult<MfaSecurityViewModel>> EnableMfaAsync(string code, CancellationToken cancellationToken = default) =>
        SendForAsync<MfaSecurityViewModel>(HttpMethod.Post, "api/auth/mfa/enable", new { Code = code }, cancellationToken);

    public Task<ApiResult<bool>> DisableMfaAsync(string code, CancellationToken cancellationToken = default) =>
        SendForBoolAsync(HttpMethod.Post, "api/auth/mfa/disable", new { Code = code }, cancellationToken);

    public Task<ApiResult<bool>> ChangePasswordAsync(ChangePasswordViewModel model, CancellationToken cancellationToken = default) =>
        SendForBoolAsync(HttpMethod.Post, "api/auth/password/change", new
        {
            model.CurrentPassword,
            model.NewPassword
        }, cancellationToken);

    public Task<ApiResult<LicenseFullViewModel>> GetLicenseAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<LicenseFullViewModel>($"api/access/licenses/{licenseId}", cancellationToken);

    public Task<ApiResult<LicenseStructureViewModel>> GetStructureAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<LicenseStructureViewModel>($"api/access/licenses/{licenseId}/structure", cancellationToken);

    public Task<ApiResult<UnitDetailViewModel>> GetUnitDetailsAsync(Guid licenseId, Guid unitId, CancellationToken cancellationToken = default) =>
        GetAsync<UnitDetailViewModel>($"api/access/licenses/{licenseId}/units/{unitId}/details", cancellationToken);

    public Task<ApiResult<PersonProfileViewModel>> GetPersonProfileAsync(Guid licenseId, Guid residentId, CancellationToken cancellationToken = default) =>
        GetAsync<PersonProfileViewModel>($"api/access/licenses/{licenseId}/residents/{residentId}/profile", cancellationToken);

    public Task<ApiResult<List<RegistrationInviteViewModel>>> GetRegistrationInvitesAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<RegistrationInviteViewModel>>($"api/access/licenses/{licenseId}/registration-invites", cancellationToken);

    public Task<ApiResult<LicenseAdministrationViewModel>> GetLicenseAdministrationAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<LicenseAdministrationViewModel>($"api/access/licenses/{licenseId}/administration", cancellationToken);

    public Task<ApiResult<PublicRegistrationInviteViewModel>> GetPublicRegistrationInviteAsync(string token, CancellationToken cancellationToken = default) =>
        GetAsync<PublicRegistrationInviteViewModel>($"api/public/registration-invites/{Uri.EscapeDataString(token)}", cancellationToken);

    public Task<ApiResult<List<AccessDeviceRowViewModel>>> GetDevicesAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<AccessDeviceRowViewModel>>($"api/access/licenses/{licenseId}/devices", cancellationToken);

    public Task<ApiResult<List<DeviceActionRowViewModel>>> GetDeviceActionsAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<DeviceActionRowViewModel>>($"api/access/licenses/{licenseId}/devices/actions", cancellationToken);

    public Task<ApiResult<List<CredentialRowViewModel>>> GetCredentialsAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<CredentialRowViewModel>>($"api/access/licenses/{licenseId}/credentials", cancellationToken);

    public Task<ApiResult<List<AccessEventRowViewModel>>> GetAccessEventsAsync(Guid licenseId, Guid deviceId, int take = 50, CancellationToken cancellationToken = default) =>
        GetAsync<List<AccessEventRowViewModel>>($"api/access/licenses/{licenseId}/devices/{deviceId}/access-events?take={take}", cancellationToken);

    public Task<ApiResult<List<CftvDeviceRowViewModel>>> GetCamerasAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<CftvDeviceRowViewModel>>($"api/access/licenses/{licenseId}/cftv", cancellationToken);

    public Task<ApiResult<List<DeliveryRowViewModel>>> GetDeliveriesAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<DeliveryRowViewModel>>($"api/access/licenses/{licenseId}/deliveries", cancellationToken);

    public Task<ApiResult<ConciergeDashboardViewModel>> GetConciergeDashboardAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<ConciergeDashboardViewModel>($"api/access/licenses/{licenseId}/concierge", cancellationToken);

    public Task<ApiResult<ConciergeVisitViewModel>> CreateConciergeVisitAsync(Guid licenseId, ConciergeVisitFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<ConciergeVisitViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/concierge/visits", new
        {
            model.HostResidentId,
            model.VisitorName,
            model.Document,
            model.PhoneNumber,
            model.Company,
            model.Purpose,
            model.VehiclePlate,
            model.ImageBase64,
            model.CredentialType,
            model.ValidFrom,
            model.ValidTo,
            model.MaxUses,
            model.RouteIds,
            model.RequireApproval,
            model.RepeatCount,
            model.RepeatEveryDays,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        }, cancellationToken);

    public Task<ApiResult<ConciergeVisitViewModel>> UpdateConciergeVisitStatusAsync(Guid licenseId, Guid visitId, int status, string reason, CancellationToken cancellationToken = default) =>
        SendForAsync<ConciergeVisitViewModel>(HttpMethod.Patch, $"api/access/licenses/{licenseId}/concierge/visits/{visitId}/status", new { Status = status, Reason = reason }, cancellationToken);

    public Task<ApiResult<ConciergeVisitViewModel>> DecideConciergeVisitAsync(Guid licenseId, Guid visitId, bool approved, string notes, CancellationToken cancellationToken = default) =>
        SendForAsync<ConciergeVisitViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/concierge/visits/{visitId}/approval", new { Approved = approved, Notes = notes }, cancellationToken);

    public Task<ApiResult<AccessWatchlistEntryViewModel>> AddWatchlistEntryAsync(Guid licenseId, AccessWatchlistFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<AccessWatchlistEntryViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/concierge/watchlist", model, cancellationToken);

    public Task<ApiResult<bool>> RemoveWatchlistEntryAsync(Guid licenseId, Guid entryId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/concierge/watchlist/{entryId}", cancellationToken);

    public Task<ApiResult<List<GlobalResidentSearchViewModel>>> SearchResidentsAsync(
        string? query,
        string? document,
        string? phone,
        string? credential,
        string? unit,
        string? licenseId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["query"] = query,
            ["document"] = document,
            ["phone"] = phone,
            ["credential"] = credential,
            ["unit"] = unit,
            ["licenseId"] = licenseId
        };
        var queryString = string.Join("&", parameters
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!.Trim())}"));
        return GetAsync<List<GlobalResidentSearchViewModel>>($"api/access/operations/residents/search?{queryString}", cancellationToken);
    }

    public async Task<ApiResult<Guid>> CreateLicenseAsync(CreateLicenseViewModel model, CancellationToken cancellationToken = default)
    {
        var enterpriseId = await _sessionContext.GetEnterpriseIdAsync(cancellationToken);
        if (!Guid.TryParse(enterpriseId, out _))
            return ApiResult<Guid>.Fail("Nao foi possivel identificar a empresa da sessao. Entre novamente.");

        var payload = new
        {
            EnterpriseId = enterpriseId,
            Name = model.Name.Trim(),
            CNPJ = OnlyDigits(model.CNPJ),
            City = model.City.Trim(),
            Country = model.Country.Trim(),
            Code = string.IsNullOrWhiteSpace(model.Code) ? $"LIC-{DateTime.UtcNow:yyyyMMddHHmmss}" : model.Code.Trim(),
            model.Organization,
            model.Building,
            model.Type,
            Location = new { Name = model.City.Trim(), X = model.LocationX, Y = model.LocationY },
            model.ExpireDate
        };

        var response = await SendAsync(HttpMethod.Post, "api/access/licenses/by-enterprise", payload, false, cancellationToken);
        if (!response.Success || response.Document is null)
            return ApiResult<Guid>.Fail(response.Error!, response.StatusCode);

        using (response.Document)
        {
            if (response.Document.RootElement.TryGetProperty("license", out var license) &&
                license.TryGetProperty("id", out var id) &&
                Guid.TryParse(id.GetString(), out var createdId))
            {
                return ApiResult<Guid>.Ok(createdId, response.StatusCode);
            }
        }

        return ApiResult<Guid>.Fail("A licenca foi criada, mas a API nao retornou o identificador.", response.StatusCode);
    }

    public Task<ApiResult<bool>> CreateBlockAsync(Guid licenseId, BlockFormViewModel model, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/blocks", new { Name = model.Name?.Trim() ?? string.Empty }, false, cancellationToken);

    public Task<ApiResult<bool>> UpdateBlockAsync(Guid licenseId, Guid blockId, BlockFormViewModel model, CancellationToken cancellationToken = default) =>
        PatchAsync($"api/access/licenses/{licenseId}/blocks/{blockId}", new { Name = model.Name?.Trim() ?? string.Empty }, cancellationToken);

    public Task<ApiResult<bool>> DeleteBlockAsync(Guid licenseId, Guid blockId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/blocks/{blockId}", cancellationToken);

    public Task<ApiResult<bool>> CreateUnitAsync(Guid licenseId, UnitFormViewModel model, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/units", new
        {
            model.BlockId,
            Number = model.Number?.Trim() ?? string.Empty,
            Floor = model.Floor?.Trim() ?? string.Empty
        }, false, cancellationToken);

    public Task<ApiResult<bool>> UpdateUnitAsync(Guid licenseId, Guid unitId, UnitFormViewModel model, CancellationToken cancellationToken = default) =>
        PatchAsync($"api/access/licenses/{licenseId}/units/{unitId}", new
        {
            model.BlockId,
            Number = model.Number?.Trim() ?? string.Empty,
            Floor = model.Floor?.Trim() ?? string.Empty
        }, cancellationToken);

    public Task<ApiResult<bool>> DeleteUnitAsync(Guid licenseId, Guid unitId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/units/{unitId}", cancellationToken);

    public Task<ApiResult<bool>> CreateResidentAsync(Guid licenseId, ResidentFormViewModel model, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/residents", new
        {
            model.UnitId,
            Name = model.Name.Trim(),
            Email = model.Email.Trim(),
            PhoneNumber = model.PhoneNumber.Trim(),
            CommercialPhone = model.CommercialPhone.Trim(),
            CPF = model.CPF.Trim(),
            RG = model.RG.Trim(),
            Description = model.Description.Trim(),
            ApartmentNumber = model.ApartmentNumber.Trim(),
            model.AccessType,
            model.Relationship,
            model.NotifyAccess,
            model.Temporary,
            model.Expire
        }, false, cancellationToken);

    public Task<ApiResult<bool>> UpdateStructureSettingsAsync(Guid licenseId, StructureSettingsViewModel model, CancellationToken cancellationToken = default) =>
        PatchAsync($"api/access/licenses/{licenseId}/structure/settings", new
        {
            GroupLabelSingular = model.GroupLabelSingular.Trim(),
            GroupLabelPlural = model.GroupLabelPlural.Trim(),
            UnitLabelSingular = model.UnitLabelSingular.Trim(),
            UnitLabelPlural = model.UnitLabelPlural.Trim()
        }, cancellationToken);

    public Task<ApiResult<PersonProfileViewModel>> UpdatePersonProfileAsync(Guid licenseId, Guid residentId, PersonProfileFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<PersonProfileViewModel>(HttpMethod.Patch, $"api/access/licenses/{licenseId}/residents/{residentId}/profile", new
        {
            model.Name,
            model.Email,
            model.PhoneNumber,
            model.CommercialPhone,
            model.CPF,
            model.RG,
            model.BirthDate,
            model.Description,
            model.ImageUrl,
            model.NotifyAccess,
            model.IsActive,
            model.Relationship
        }, cancellationToken);

    public Task<ApiResult<VehicleViewModel>> CreateVehicleAsync(Guid licenseId, Guid residentId, VehicleFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<VehicleViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/residents/{residentId}/vehicles", new
        {
            model.UnitId,
            model.Plate,
            model.Brand,
            model.Model,
            model.Color,
            model.Type,
            model.TagIdentifier
        }, cancellationToken);

    public Task<ApiResult<VehicleViewModel>> UpdateVehicleAsync(Guid licenseId, Guid residentId, Guid vehicleId, VehicleFormViewModel model, bool isActive, CancellationToken cancellationToken = default) =>
        SendForAsync<VehicleViewModel>(HttpMethod.Patch, $"api/access/licenses/{licenseId}/residents/{residentId}/vehicles/{vehicleId}", new
        {
            model.UnitId,
            model.Plate,
            model.Brand,
            model.Model,
            model.Color,
            model.Type,
            model.TagIdentifier,
            IsActive = isActive
        }, cancellationToken);

    public Task<ApiResult<bool>> DeleteVehicleAsync(Guid licenseId, Guid residentId, Guid vehicleId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/residents/{residentId}/vehicles/{vehicleId}", cancellationToken);

    public Task<ApiResult<bool>> DeleteResidentAsync(Guid licenseId, Guid residentId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/residents/{residentId}", cancellationToken);

    public Task<ApiResult<StructureImportPreviewViewModel>> PreviewStructureImportAsync(
        Guid licenseId,
        string fileName,
        string content,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendForAsync<StructureImportPreviewViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/imports/structure/preview",
            new { FileName = fileName, Content = content, IdempotencyKey = idempotencyKey },
            cancellationToken);

    public Task<ApiResult<StructureImportExecutionViewModel>> ExecuteStructureImportAsync(
        Guid licenseId,
        string fileName,
        string content,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendForAsync<StructureImportExecutionViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/imports/structure/execute",
            new { FileName = fileName, Content = content, IdempotencyKey = idempotencyKey },
            cancellationToken);

    public Task<ApiResult<List<RecycleBinItemViewModel>>> GetRecycleBinAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<List<RecycleBinItemViewModel>>(
            $"api/access/licenses/{licenseId}/recycle-bin",
            cancellationToken);

    public Task<ApiResult<RecycleBinRestoreViewModel>> RestoreRecycleBinItemAsync(
        Guid licenseId,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        SendForAsync<RecycleBinRestoreViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/recycle-bin/{itemId}/restore",
            new { },
            cancellationToken);

    public Task<ApiResult<bool>> PurgeRecycleBinItemAsync(
        Guid licenseId,
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/recycle-bin/{itemId}", cancellationToken);

    public Task<ApiResult<List<ConfigurationBackupViewModel>>> GetConfigurationBackupsAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<List<ConfigurationBackupViewModel>>(
            $"api/access/licenses/{licenseId}/backups",
            cancellationToken);

    public Task<ApiResult<ConfigurationBackupViewModel>> CreateConfigurationBackupAsync(
        Guid licenseId,
        ConfigurationBackupCreateViewModel model,
        CancellationToken cancellationToken = default) =>
        SendForAsync<ConfigurationBackupViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/backups",
            new { model.Name, model.Description },
            cancellationToken);

    public Task<ApiResult<ConfigurationRestorePreviewViewModel>> PreviewConfigurationRestoreAsync(
        Guid licenseId,
        Guid backupId,
        ConfigurationRestoreOptionsViewModel model,
        CancellationToken cancellationToken = default) =>
        SendForAsync<ConfigurationRestorePreviewViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/backups/{backupId}/preview",
            new { model.Mode, model.IncludeDevices, model.IncludeRoutes, model.IncludeCredentials },
            cancellationToken);

    public Task<ApiResult<ConfigurationRestoreExecutionViewModel>> RestoreConfigurationBackupAsync(
        Guid licenseId,
        Guid backupId,
        ConfigurationRestoreOptionsViewModel model,
        CancellationToken cancellationToken = default) =>
        SendForAsync<ConfigurationRestoreExecutionViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/backups/{backupId}/restore",
            new
            {
                model.Mode,
                model.IncludeDevices,
                model.IncludeRoutes,
                model.IncludeCredentials,
                model.Confirmation
            },
            cancellationToken);

    public Task<ApiResult<bool>> DeleteConfigurationBackupAsync(
        Guid licenseId,
        Guid backupId,
        CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/backups/{backupId}", cancellationToken);

    public Task<ApiResult<BackupAutomationPolicyViewModel>> GetBackupAutomationAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        GetAsync<BackupAutomationPolicyViewModel>(
            $"api/access/licenses/{licenseId}/backups/automation",
            cancellationToken);

    public Task<ApiResult<BackupAutomationPolicyViewModel>> UpdateBackupAutomationAsync(
        Guid licenseId,
        BackupAutomationPolicyViewModel model,
        CancellationToken cancellationToken = default) =>
        SendForAsync<BackupAutomationPolicyViewModel>(
            HttpMethod.Put,
            $"api/access/licenses/{licenseId}/backups/automation",
            new
            {
                model.Enabled,
                model.IntervalHours,
                model.ExportEnabled,
                model.ExternalRetentionDays
            },
            cancellationToken);

    public Task<ApiResult<BackupRunViewModel>> RunBackupNowAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default) =>
        SendForAsync<BackupRunViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/backups/run",
            new { },
            cancellationToken);

    public Task<ApiResult<BackupArchiveViewModel>> BuildBackupArchiveAsync(
        Guid licenseId,
        Guid backupId,
        CancellationToken cancellationToken = default) =>
        SendForAsync<BackupArchiveViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/backups/{backupId}/archive",
            new { },
            cancellationToken);

    public Task<ApiResult<ConfigurationBackupViewModel>> ImportBackupArchiveAsync(
        Guid licenseId,
        string fileName,
        string contentBase64,
        CancellationToken cancellationToken = default) =>
        SendForAsync<ConfigurationBackupViewModel>(
            HttpMethod.Post,
            $"api/access/licenses/{licenseId}/backups/import",
            new { FileName = fileName, ContentBase64 = contentBase64 },
            cancellationToken);

    public Task<ApiResult<bool>> SetVehicleStatusAsync(Guid licenseId, Guid residentId, Guid vehicleId, bool isActive, CancellationToken cancellationToken = default) =>
        PatchAsync($"api/access/licenses/{licenseId}/residents/{residentId}/vehicles/{vehicleId}/status", new { IsActive = isActive }, cancellationToken);

    public Task<ApiResult<RegistrationInviteViewModel>> CreateRegistrationInviteAsync(Guid licenseId, Guid residentId, string contact, int channel = 3, int validDays = 7, CancellationToken cancellationToken = default) =>
        SendForAsync<RegistrationInviteViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/residents/{residentId}/registration-invites", new
        {
            Contact = contact.Trim(),
            Channel = channel,
            ValidDays = validDays
        }, cancellationToken);

    public Task<ApiResult<PublicRegistrationInviteViewModel>> CompleteRegistrationInviteAsync(string token, CompleteRegistrationInviteViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<PublicRegistrationInviteViewModel>(HttpMethod.Post, $"api/public/registration-invites/{Uri.EscapeDataString(token)}/complete", new
        {
            model.Name,
            model.Email,
            model.PhoneNumber,
            model.CPF,
            model.RG,
            model.BirthDate
        }, cancellationToken);

    public Task<ApiResult<AccessDeviceCreationViewModel>> CreateDeviceAsync(Guid licenseId, AccessDeviceFormViewModel model, CancellationToken cancellationToken = default)
    {
        var option = DeviceCatalog.Find(model.Type);
        if (option is null)
            return Task.FromResult(ApiResult<AccessDeviceCreationViewModel>.Fail("Selecione uma marca e um modelo validos."));

        return SendForAsync<AccessDeviceCreationViewModel>(HttpMethod.Post, "api/access/devices/by-license", new
        {
            LicenseId = licenseId.ToString(),
            Name = model.Name.Trim(),
            IPAddress = model.IPAddress.Trim(),
            model.Port,
            Username = model.Username.Trim(),
            model.Password,
            Model = DeviceCatalog.ApiModel(model.Type),
            model.Type,
            model.IsActive,
            Location = new { Name = model.Name.Trim(), X = model.LocationX, Y = model.LocationY }
        }, true, cancellationToken);
    }

    public Task<ApiResult<bool>> UpdateDeviceAsync(Guid licenseId, Guid deviceId, AccessDeviceFormViewModel model, CancellationToken cancellationToken = default) =>
        PatchAsync($"api/access/licenses/{licenseId}/devices/{deviceId}", new
        {
            model.Name,
            model.IPAddress,
            model.Port,
            model.Username,
            model.Password,
            model.IsActive
        }, cancellationToken);

    public Task<ApiResult<bool>> DeleteDeviceAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/devices/{deviceId}", cancellationToken);

    public Task<ApiResult<bool>> TestDeviceConnectionAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/devices/{deviceId}/test-connection", new { }, false, cancellationToken);

    public Task<ApiResult<bool>> OpenDoorAsync(Guid licenseId, Guid deviceId, int channel, string reason, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/devices/{deviceId}/open-door", new { Channel = channel, Reason = reason }, false, cancellationToken);

    public Task<ApiResult<CredentialOperationViewModel>> CreateCredentialAsync(Guid licenseId, CredentialFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<CredentialOperationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/credentials", new
        {
            model.ResidentId,
            model.DeviceId,
            model.Type,
            model.Identifier,
            model.ImageBase64,
            model.ValidFrom,
            model.ValidTo,
            model.IsTemporary,
            model.MaxRenewals,
            model.MaxUses
        }, cancellationToken);

    public Task<ApiResult<CredentialOperationViewModel>> RenewCredentialAsync(Guid licenseId, Guid credentialId, CancellationToken cancellationToken = default) =>
        SendForAsync<CredentialOperationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/credentials/{credentialId}/renew", new { }, cancellationToken);

    public Task<ApiResult<LicenseUserAccessViewModel>> CreateLicenseUserAsync(Guid licenseId, LicenseUserFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<LicenseUserAccessViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/administration/users", new
        {
            model.Name, model.Email, model.PhoneNumber, model.Password, model.Role,
            Permissions = model.Permissions == 0 ? (long?)null : model.Permissions
        }, cancellationToken);

    public Task<ApiResult<LicenseUserAccessViewModel>> UpdateLicenseUserAsync(Guid licenseId, Guid accessId, LicenseUserFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<LicenseUserAccessViewModel>(HttpMethod.Patch, $"api/access/licenses/{licenseId}/administration/users/{accessId}", new
        {
            model.Role, model.Permissions, model.IsActive
        }, cancellationToken);

    public Task<ApiResult<CredentialPolicyViewModel>> UpdateCredentialPolicyAsync(Guid licenseId, CredentialPolicyViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<CredentialPolicyViewModel>(HttpMethod.Patch, $"api/access/licenses/{licenseId}/administration/credential-policy", model, cancellationToken);

    public Task<ApiResult<CredentialOperationViewModel>> SetCredentialStatusAsync(Guid licenseId, Guid credentialId, bool isActive, CancellationToken cancellationToken = default) =>
        SendForAsync<CredentialOperationViewModel>(HttpMethod.Patch, $"api/access/licenses/{licenseId}/credentials/{credentialId}/status", new { IsActive = isActive }, cancellationToken);

    public Task<ApiResult<CredentialOperationViewModel>> RestoreCredentialAsync(Guid licenseId, Guid credentialId, Guid deviceId, string? imageBase64 = null, CancellationToken cancellationToken = default) =>
        SendForAsync<CredentialOperationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/credentials/{credentialId}/restore", new { DeviceId = deviceId, ImageBase64 = imageBase64 }, cancellationToken);

    public Task<ApiResult<bool>> StartFaceEnrollmentAsync(Guid licenseId, Guid credentialId, Guid deviceId, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/credentials/{credentialId}/face-enrollment", new { DeviceId = deviceId }, false, cancellationToken);

    public Task<ApiResult<bool>> CancelFaceEnrollmentAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/devices/{deviceId}/face-enrollment/cancel", new { }, false, cancellationToken);

    public Task<ApiResult<bool>> RemoveCredentialFromDeviceAsync(Guid licenseId, Guid credentialId, Guid deviceId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/credentials/{credentialId}/devices/{deviceId}", cancellationToken);

    public Task<ApiResult<CredentialOperationViewModel>> DeleteCredentialAsync(Guid licenseId, Guid credentialId, CancellationToken cancellationToken = default) =>
        SendForAsync<CredentialOperationViewModel>(HttpMethod.Delete, $"api/access/licenses/{licenseId}/credentials/{credentialId}", new { }, cancellationToken);

    public Task<ApiResult<List<AccessRouteViewModel>>> GetAccessRoutesAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<AccessRouteViewModel>>($"api/access/licenses/{licenseId}/routes", cancellationToken);

    public Task<ApiResult<ResidentRouteResolutionViewModel>> GetResidentRouteResolutionAsync(Guid licenseId, Guid residentId, CancellationToken cancellationToken = default) =>
        GetAsync<ResidentRouteResolutionViewModel>($"api/access/licenses/{licenseId}/routes/resolution/residents/{residentId}", cancellationToken);

    public Task<ApiResult<AccessRouteViewModel>> SaveAccessRouteAsync(Guid licenseId, Guid? routeId, AccessRouteFormViewModel model, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            Name = model.Name.Trim(),
            Description = model.Description.Trim(),
            model.Audience,
            model.IsActive,
            model.AllowTemporary,
            model.DaysOfWeekMask,
            StartTime = model.StartTime ?? TimeSpan.Zero,
            EndTime = model.EndTime ?? new TimeSpan(23, 59, 59),
            Devices = model.Devices.Where(x => x.Selected).Select(x => new
            {
                x.DeviceId,
                x.PortalNumber,
                x.Direction,
                x.IsActive
            })
        };
        return SendForAsync<AccessRouteViewModel>(routeId.HasValue ? HttpMethod.Put : HttpMethod.Post,
            routeId.HasValue ? $"api/access/licenses/{licenseId}/routes/{routeId}" : $"api/access/licenses/{licenseId}/routes",
            payload,
            cancellationToken);
    }

    public Task<ApiResult<bool>> DeleteAccessRouteAsync(Guid licenseId, Guid routeId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/routes/{routeId}", cancellationToken);

    public Task<ApiResult<List<AmenityViewModel>>> GetAmenitiesAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<AmenityViewModel>>($"api/access/licenses/{licenseId}/amenities", cancellationToken);

    public Task<ApiResult<List<AmenityUnitSearchViewModel>>> SearchAmenityUnitsAsync(Guid licenseId, string query, CancellationToken cancellationToken = default) =>
        GetAsync<List<AmenityUnitSearchViewModel>>($"api/access/licenses/{licenseId}/amenities/unit-search?query={Uri.EscapeDataString(query)}", cancellationToken);

    public Task<ApiResult<AmenityViewModel>> SaveAmenityAsync(Guid licenseId, Guid? amenityId, AmenityFormViewModel model, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            Name = model.Name.Trim(),
            Description = model.Description.Trim(),
            model.Capacity,
            model.Active,
            model.FeeAmount,
            FeeDescription = model.FeeDescription.Trim(),
            model.RequiresApproval,
            model.RequiresTermsAcceptance,
            TermsText = model.TermsText.Trim(),
            model.MonthlyLimitPerUnit,
            model.MinAdvanceNoticeHours,
            model.MaxAdvanceDays,
            model.CancellationCutoffHours,
            ScheduleSlots = model.ScheduleSlots.Select(x => new { x.Id, x.DayOfWeek, x.StartTime, x.EndTime, x.Active }),
            Blackouts = model.Blackouts.Select(x => new { x.Id, x.StartDate, x.EndDate, Reason = x.Reason.Trim() })
        };
        return SendForAsync<AmenityViewModel>(amenityId.HasValue ? HttpMethod.Put : HttpMethod.Post,
            amenityId.HasValue ? $"api/access/licenses/{licenseId}/amenities/{amenityId}" : $"api/access/licenses/{licenseId}/amenities",
            payload,
            cancellationToken);
    }

    public Task<ApiResult<bool>> DeleteAmenityAsync(Guid licenseId, Guid amenityId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/amenities/{amenityId}", cancellationToken);

    public Task<ApiResult<List<AmenitySlotAvailabilityViewModel>>> GetAmenityAvailabilityAsync(
        Guid licenseId,
        Guid amenityId,
        DateTime date,
        Guid? excludeBookingId = null,
        CancellationToken cancellationToken = default)
    {
        var exclude = excludeBookingId.HasValue ? $"&excludeBookingId={excludeBookingId}" : string.Empty;
        return GetAsync<List<AmenitySlotAvailabilityViewModel>>(
            $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/availability?date={date:yyyy-MM-dd}{exclude}",
            cancellationToken);
    }

    public Task<ApiResult<List<AmenityBookingViewModel>>> GetAmenityBookingsAsync(Guid licenseId, Guid amenityId, DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        GetAsync<List<AmenityBookingViewModel>>($"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", cancellationToken);

    public Task<ApiResult<List<AmenityBookingViewModel>>> GetLicenseAmenityBookingsAsync(
        Guid licenseId,
        DateTime from,
        DateTime to,
        Guid? amenityId = null,
        string? status = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"from={from:yyyy-MM-dd}",
            $"to={to:yyyy-MM-dd}"
        };
        if (amenityId.HasValue) query.Add($"amenityId={amenityId}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search.Trim())}");

        return GetAsync<List<AmenityBookingViewModel>>(
            $"api/access/licenses/{licenseId}/amenity-bookings?{string.Join("&", query)}",
            cancellationToken);
    }

    public Task<ApiResult<AmenityBookingViewModel>> CreateAmenityBookingAsync(Guid licenseId, Guid amenityId, AmenityBookingFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<AmenityBookingViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings", new
        {
            model.UnitId,
            model.ResidentId,
            model.Date,
            model.SlotId,
            model.TermsAccepted,
            Notes = model.Notes.Trim()
        }, cancellationToken);

    public Task<ApiResult<AmenityBookingViewModel>> UpdateAmenityBookingAsync(
        Guid licenseId,
        Guid amenityId,
        Guid bookingId,
        AmenityBookingFormViewModel model,
        CancellationToken cancellationToken = default) =>
        SendForAsync<AmenityBookingViewModel>(
            HttpMethod.Put,
            $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/{bookingId}",
            new
            {
                model.UnitId,
                model.ResidentId,
                model.Date,
                model.SlotId,
                model.TermsAccepted,
                Notes = model.Notes.Trim()
            },
            cancellationToken);

    public Task<ApiResult<AmenityBookingViewModel>> ApproveAmenityBookingAsync(Guid licenseId, Guid amenityId, Guid bookingId, CancellationToken cancellationToken = default) =>
        SendForAsync<AmenityBookingViewModel>(HttpMethod.Put, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/{bookingId}/approve", new { }, cancellationToken);

    public Task<ApiResult<AmenityBookingViewModel>> RejectAmenityBookingAsync(Guid licenseId, Guid amenityId, Guid bookingId, string reason, CancellationToken cancellationToken = default) =>
        SendForAsync<AmenityBookingViewModel>(HttpMethod.Put, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/{bookingId}/reject", new { Reason = reason }, cancellationToken);

    public Task<ApiResult<bool>> CancelAmenityBookingAsync(Guid licenseId, Guid amenityId, Guid bookingId, string reason, CancellationToken cancellationToken = default) =>
        SendForBoolAsync(HttpMethod.Delete, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/{bookingId}", new { Reason = reason }, cancellationToken);

    public Task<ApiResult<CredentialOperationViewModel>> ActivateFacialByRoutesAsync(Guid licenseId, Guid residentId, string imageBase64, CancellationToken cancellationToken = default) =>
        SendForAsync<CredentialOperationViewModel>(HttpMethod.Post,
            $"api/access/licenses/{licenseId}/residents/{residentId}/facial/activate-by-routes",
            new { ImageBase64 = imageBase64 }, cancellationToken);

    public Task<ApiResult<DeviceInspectionViewModel>> InspectDeviceAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default) =>
        SendForAsync<DeviceInspectionViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/devices/{deviceId}/inspect", new { }, cancellationToken);

    public Task<ApiResult<List<DeviceInventoryItemViewModel>>> GetDeviceInventoryAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default) =>
        GetAsync<List<DeviceInventoryItemViewModel>>($"api/access/licenses/{licenseId}/access-control/devices/{deviceId}/inventory", cancellationToken);

    public Task<ApiResult<DeviceInventorySummaryViewModel>> RefreshDeviceInventoryAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default) =>
        SendForAsync<DeviceInventorySummaryViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/devices/{deviceId}/inventory/refresh", new { }, cancellationToken);

    public Task<ApiResult<AccessBatchOperationViewModel>> RepairDeviceInventoryAsync(Guid licenseId, Guid deviceId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) =>
        SendForAsync<AccessBatchOperationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/devices/{deviceId}/inventory/repair",
            new { InventoryItemIds = itemIds, IdempotencyKey = Guid.NewGuid().ToString("N") }, cancellationToken);

    public Task<ApiResult<ReconciliationPreviewViewModel>> PreviewReconciliationAsync(Guid licenseId, IReadOnlyCollection<Guid>? credentialIds = null, CancellationToken cancellationToken = default) =>
        SendForAsync<ReconciliationPreviewViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/reconciliation/preview", new { CredentialIds = credentialIds ?? [] }, cancellationToken);

    public Task<ApiResult<RouteSimulationViewModel>> SimulateRouteAsync(Guid licenseId, Guid residentId, int credentialType, CancellationToken cancellationToken = default) =>
        SendForAsync<RouteSimulationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/routes/simulate", new { ResidentId = residentId, CredentialType = credentialType }, cancellationToken);

    public Task<ApiResult<AccessBatchOperationViewModel>> QueueReconciliationAsync(Guid licenseId, IReadOnlyCollection<Guid>? credentialIds = null, CancellationToken cancellationToken = default) =>
        SendForAsync<AccessBatchOperationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/reconciliation/batches", new { CredentialIds = credentialIds ?? [], IdempotencyKey = Guid.NewGuid().ToString("N"), Priority = 50 }, cancellationToken);

    public Task<ApiResult<List<AccessBatchOperationViewModel>>> GetReconciliationBatchesAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<AccessBatchOperationViewModel>>($"api/access/licenses/{licenseId}/access-control/reconciliation/batches", cancellationToken);

    public Task<ApiResult<bool>> CancelReconciliationBatchAsync(Guid licenseId, Guid batchId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/access-control/reconciliation/batches/{batchId}", cancellationToken);

    public Task<ApiResult<AccessBatchOperationViewModel>> RetryReconciliationBatchAsync(Guid licenseId, Guid batchId, CancellationToken cancellationToken = default) =>
        SendForAsync<AccessBatchOperationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/reconciliation/batches/{batchId}/retry", new { }, cancellationToken);

    public Task<ApiResult<List<AccessAuditViewModel>>> GetAccessAuditsAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<AccessAuditViewModel>>($"api/access/licenses/{licenseId}/access-control/audits?take=500", cancellationToken);

    public Task<ApiResult<CredentialBackupViewModel>> ExportCredentialBackupAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<CredentialBackupViewModel>($"api/access/licenses/{licenseId}/access-control/credentials/backup", cancellationToken);

    public Task<ApiResult<AccessBatchOperationViewModel>> RestoreCredentialBackupAsync(Guid licenseId, CredentialBackupViewModel backup, CancellationToken cancellationToken = default) =>
        SendForAsync<AccessBatchOperationViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/access-control/credentials/backup/restore", backup, cancellationToken);

    public Task<ApiResult<List<ResidentRouteOverrideViewModel>>> GetResidentRouteOverridesAsync(Guid licenseId, Guid residentId, CancellationToken cancellationToken = default) =>
        GetAsync<List<ResidentRouteOverrideViewModel>>($"api/access/licenses/{licenseId}/access-control/residents/{residentId}/route-overrides", cancellationToken);

    public Task<ApiResult<bool>> SaveResidentRouteOverrideAsync(Guid licenseId, Guid residentId, Guid routeId, int mode, string reason, CancellationToken cancellationToken = default) =>
        SendForBoolAsync(HttpMethod.Put, $"api/access/licenses/{licenseId}/access-control/residents/{residentId}/route-overrides", new { RouteId = routeId, Mode = mode, Reason = reason }, cancellationToken);

    public Task<ApiResult<bool>> DeleteResidentRouteOverrideAsync(Guid licenseId, Guid residentId, Guid routeId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/access-control/residents/{residentId}/route-overrides/{routeId}", cancellationToken);

    public Task<ApiResult<bool>> CreateCameraAsync(Guid licenseId, CftvDeviceFormViewModel model, CancellationToken cancellationToken = default) =>
        PostAsync("api/access/cftv/by-license", new
        {
            LicenseId = licenseId.ToString(),
            Name = model.Name.Trim(),
            IpAddress = model.IpAddress.Trim(),
            UserName = model.UserName.Trim(),
            model.Password,
            model.HTTPPort,
            model.RTSPPort,
            model.IpType,
            model.Proportion,
            model.Mark,
            model.DeviceType,
            model.MaxChannels,
            Channels = Enumerable.Range(1, Math.Max(1, model.MaxChannels)).Select(channel => new
            {
                ChannelNumber = channel,
                Name = $"Canal {channel}",
                RtspPath = string.Empty,
                IsEnabled = true
            })
        }, true, cancellationToken);

    public Task<ApiResult<bool>> CreateDeliveryAsync(Guid licenseId, DeliveryFormViewModel model, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/deliveries", new
        {
            model.Type,
            Name = model.Name.Trim(),
            Description = model.Description.Trim(),
            TrackingCode = model.TrackingCode.Trim(),
            PhotoUrl = model.PhotoUrl.Trim(),
            ReceivedBy = model.ReceivedBy.Trim()
        }, false, cancellationToken);

    public Task<ApiResult<bool>> UpdateDeliveryStatusAsync(Guid licenseId, Guid deliveryId, int status, string personName, CancellationToken cancellationToken = default) =>
        PatchAsync($"api/access/licenses/{licenseId}/deliveries/{deliveryId}/status", new { Status = status, PersonName = personName }, cancellationToken);

    private async Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var client = await CreateClientAsync();
            using var response = await client.GetAsync(BuildApiUrl(path), cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ApiResult<T>.Fail(await ReadErrorAsync(response, "Nao foi possivel carregar os dados."), response.StatusCode);

            var value = await response.Content.ReadFromJsonAsync<T>(GetJsonOptions, cancellationToken: cancellationToken);
            return value is null
                ? ApiResult<T>.Fail("A API retornou uma resposta vazia.", response.StatusCode)
                : ApiResult<T>.Ok(value, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao consultar {ApiPath}", path);
            return ApiResult<T>.Fail("A API esta indisponivel. Verifique se ela e o banco de dados estao em execucao.");
        }
    }

    private async Task<ApiResult<bool>> PostAsync(string path, object payload, bool includeApiKey, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Post, path, payload, includeApiKey, cancellationToken);
        response.Document?.Dispose();
        return response.Success
            ? ApiResult<bool>.Ok(true, response.StatusCode)
            : ApiResult<bool>.Fail(response.Error!, response.StatusCode);
    }

    private async Task<ApiResult<bool>> PatchAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Patch, path, payload, false, cancellationToken);
        response.Document?.Dispose();
        return response.Success
            ? ApiResult<bool>.Ok(true, response.StatusCode)
            : ApiResult<bool>.Fail(response.Error!, response.StatusCode);
    }

    private async Task<ApiResult<bool>> SendForBoolAsync(HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, payload, false, cancellationToken);
        response.Document?.Dispose();
        return response.Success
            ? ApiResult<bool>.Ok(true, response.StatusCode)
            : ApiResult<bool>.Fail(response.Error!, response.StatusCode);
    }

    private Task<ApiResult<T>> SendForAsync<T>(HttpMethod method, string path, object payload, bool includeApiKey, CancellationToken cancellationToken) =>
        SendForAsyncCore<T>(method, path, payload, includeApiKey, cancellationToken);

    private Task<ApiResult<T>> SendForAsync<T>(HttpMethod method, string path, object payload, CancellationToken cancellationToken) =>
        SendForAsyncCore<T>(method, path, payload, false, cancellationToken);

    private async Task<ApiResult<T>> SendForAsyncCore<T>(HttpMethod method, string path, object payload, bool includeApiKey, CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, payload, includeApiKey, cancellationToken);
        if (!response.Success || response.Document is null)
        {
            response.Document?.Dispose();
            return ApiResult<T>.Fail(response.Error ?? "A API retornou uma resposta vazia.", response.StatusCode);
        }

        using (response.Document)
        {
            var value = response.Document.RootElement.Deserialize<T>(GetJsonOptions);
            return value is null
                ? ApiResult<T>.Fail("Nao foi possivel interpretar a resposta da API.", response.StatusCode)
                : ApiResult<T>.Ok(value, response.StatusCode);
        }
    }

    private async Task<ApiResult<bool>> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Delete, path, new { }, false, cancellationToken);
        response.Document?.Dispose();
        return response.Success
            ? ApiResult<bool>.Ok(true, response.StatusCode)
            : ApiResult<bool>.Fail(response.Error!, response.StatusCode);
    }

    private async Task<(bool Success, JsonDocument? Document, string? Error, System.Net.HttpStatusCode? StatusCode)> SendAsync(
        HttpMethod method,
        string path,
        object payload,
        bool includeApiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = await CreateClientAsync();
            if (includeApiKey && GetApiKey() is { Length: > 0 } apiKey)
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);

            using var request = new HttpRequestMessage(method, BuildApiUrl(path))
            {
                Content = JsonContent.Create(payload)
            };
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (false, null, await ReadErrorAsync(response, "Nao foi possivel concluir a operacao."), response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonDocument? document = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    document = JsonDocument.Parse(body);
                }
                catch (JsonException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "A API retornou conteudo invalido para {ApiPath}. Content-Type: {ContentType}",
                        path,
                        response.Content.Headers.ContentType);
                    return (false, null, "A API retornou uma resposta invalida.", response.StatusCode);
                }
            }
            return (true, document, null, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar dados para {ApiPath}", path);
            return (false, null, "A API está indisponível. Tente novamente em instantes.", null);
        }
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        var client = _httpClientFactory.CreateClient();
        var token = await _sessionContext.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private string BuildApiUrl(string path)
    {
        var baseUrl = _configuration["CondotifyApi:BaseUrl"] ?? "https://localhost:7118";
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private string? GetApiKey()
    {
        var configured = _configuration["CondotifyApi:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Environment.GetEnvironmentVariable("CONDOTIFY_API_KEY")
            ?? Environment.GetEnvironmentVariable("CT_UserAccess_API_KEY");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            return ErrorFallback(response.StatusCode, fallback);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var modelErrors) || root.TryGetProperty("Errors", out modelErrors))
                return FormatErrors(modelErrors, fallback);
            if (root.TryGetProperty("message", out var message) || root.TryGetProperty("Message", out message))
                return message.GetString() ?? fallback;
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                var text = detail.GetString() ?? fallback;
                if (root.TryGetProperty("traceId", out var traceId) && traceId.ValueKind == JsonValueKind.String)
                    return $"{text} Codigo: {traceId.GetString()}.";
                return text;
            }
            if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                return title.GetString() ?? fallback;
        }
        catch (JsonException)
        {
            return ErrorFallback(response.StatusCode, fallback);
        }

        return ErrorFallback(response.StatusCode, fallback);
    }

    private static string ErrorFallback(System.Net.HttpStatusCode statusCode, string fallback) => statusCode switch
    {
        System.Net.HttpStatusCode.Unauthorized => "Sua sessão expirou. Entre novamente.",
        System.Net.HttpStatusCode.Forbidden => "Você não tem permissão para realizar esta operação.",
        System.Net.HttpStatusCode.NotFound => "O recurso solicitado não foi encontrado.",
        System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout =>
            "Não foi possível comunicar com o equipamento ou serviço. Verifique a conexão e tente novamente.",
        >= System.Net.HttpStatusCode.InternalServerError => "Ocorreu um erro interno na API. Tente novamente em instantes.",
        _ => fallback
    };

    private static string FormatErrors(JsonElement element, string fallback)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? fallback;

        if (element.ValueKind == JsonValueKind.Array)
            return string.Join("; ", element.EnumerateArray().Select(x => x.ToString()));

        if (element.ValueKind == JsonValueKind.Object)
        {
            var messages = element.EnumerateObject().SelectMany(property =>
                property.Value.ValueKind == JsonValueKind.Array
                    ? property.Value.EnumerateArray().Select(value => $"{property.Name}: {value}")
                    : [$"{property.Name}: {property.Value}"]);
            return string.Join("; ", messages);
        }

        return fallback;
    }

    private static string OnlyDigits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
