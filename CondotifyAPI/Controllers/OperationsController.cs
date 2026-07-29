using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Amenities;
using System.Security.Claims;

namespace CondotifyAPI.Controllers;

[ApiController]
[Route("api/access/operations")]
[Authorize]
public sealed class OperationsController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId))
            return Unauthorized();

        var now = DateTime.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
        var since = today.AddDays(-13);
        var permissionMap = await authorization.GetLicensePermissionsAsync(
            User,
            HttpContext.RequestAborted);
        var accessibleLicenseIds = PermissionScope(permissionMap, LicensePermissionEnum.ViewDashboard);
        var peopleLicenseIds = PermissionScope(permissionMap, LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewPeople);
        var deviceLicenseIds = PermissionScope(permissionMap, LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewDevices);
        var credentialLicenseIds = PermissionScope(permissionMap, LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewCredentials);
        var eventLicenseIds = PermissionScope(permissionMap, LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewEvents);
        var bookingLicenseIds = PermissionScope(permissionMap, LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewBookings);
        var licenses = await context.Licenses
            .AsNoTracking()
            .Where(x => x.EnterpriseId == enterpriseId && accessibleLicenseIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToListAsync();
        var licenseIds = licenses.Select(x => x.Id).ToList();

        if (licenseIds.Count == 0)
            return Ok(new OperationalDashboardOut());

        var output = new OperationalDashboardOut
        {
            LicenseCount = licenses.Count,
            ResidentCount = await context.Residents.AsNoTracking()
                .CountAsync(x => peopleLicenseIds.Contains(x.Unit.Block.LicenseId)),
            DeviceCount = await context.Devices.AsNoTracking()
                .CountAsync(x => deviceLicenseIds.Contains(x.LicenseId)),
            OnlineDeviceCount = await context.Devices.AsNoTracking()
                .CountAsync(x => deviceLicenseIds.Contains(x.LicenseId) && x.IsActive),
            CredentialCount = await context.ResidentAccessCredentials.AsNoTracking()
                .CountAsync(x => credentialLicenseIds.Contains(x.Resident.Unit.Block.LicenseId)),
            PendingCredentialCount = await context.ResidentAccessDevices.AsNoTracking()
                .CountAsync(x => credentialLicenseIds.Contains(x.Credential.Resident.Unit.Block.LicenseId) &&
                    x.SyncStatus != CredentialSyncStatusEnum.Synced &&
                    x.SyncStatus != CredentialSyncStatusEnum.Removed),
            AccessEventCount = await context.AccessEventRecords.AsNoTracking()
                .CountAsync(x => eventLicenseIds.Contains(x.LicenseId) && x.OccurredAt >= since),
            AuthorizedAccessCount = await context.AccessEventRecords.AsNoTracking()
                .CountAsync(x => eventLicenseIds.Contains(x.LicenseId) && x.OccurredAt >= since && x.Authorized),
            PendingBookingCount = await context.AmenityBookings.AsNoTracking()
                .CountAsync(x => bookingLicenseIds.Contains(x.LicenseId) && x.Status == AmenityBookingStatusEnum.Pending),
            TodayBookingCount = await context.AmenityBookings.AsNoTracking()
                .CountAsync(x => bookingLicenseIds.Contains(x.LicenseId) && x.Date == today &&
                    (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed)),
            PendingBatchCount = await context.AccessBatchOperations.AsNoTracking()
                .CountAsync(x => credentialLicenseIds.Contains(x.LicenseId) &&
                    (x.Status == AccessBatchStatusEnum.Queued || x.Status == AccessBatchStatusEnum.Running)),
            FailedBatchCount = await context.AccessBatchOperations.AsNoTracking()
                .CountAsync(x => credentialLicenseIds.Contains(x.LicenseId) &&
                    (x.Status == AccessBatchStatusEnum.Failed ||
                     x.Status == AccessBatchStatusEnum.DeadLetter ||
                     x.Status == AccessBatchStatusEnum.CompletedWithErrors))
        };

        var syncedBindings = await context.ResidentAccessDevices.AsNoTracking()
            .CountAsync(x => credentialLicenseIds.Contains(x.Credential.Resident.Unit.Block.LicenseId) &&
                x.SyncStatus == CredentialSyncStatusEnum.Synced);
        var trackedBindings = syncedBindings + output.PendingCredentialCount;
        output.SynchronizationRate = trackedBindings == 0
            ? 100
            : Math.Round(syncedBindings * 100m / trackedBindings, 1);
        output.AuthorizationRate = output.AccessEventCount == 0
            ? 0
            : Math.Round(output.AuthorizedAccessCount * 100m / output.AccessEventCount, 1);

        var trendRows = await context.AccessEventRecords.AsNoTracking()
            .Where(x => eventLicenseIds.Contains(x.LicenseId) && x.OccurredAt >= since)
            .GroupBy(x => new { x.OccurredAt.Year, x.OccurredAt.Month, x.OccurredAt.Day })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                Authorized = group.Count(x => x.Authorized),
                Denied = group.Count(x => !x.Authorized)
            })
            .ToListAsync();

        output.AccessTrend = Enumerable.Range(0, 14)
            .Select(offset => since.AddDays(offset))
            .Select(date =>
            {
                var row = trendRows.FirstOrDefault(x =>
                    x.Year == date.Year && x.Month == date.Month && x.Day == date.Day);
                return new OperationalTrendPointOut
                {
                    Date = date,
                    Authorized = row?.Authorized ?? 0,
                    Denied = row?.Denied ?? 0
                };
            })
            .ToList();

        output.RecentActivities = await context.AccessOperationAudits.AsNoTracking()
            .Where(x => eventLicenseIds.Contains(x.LicenseId))
            .OrderByDescending(x => x.CreatedAt)
            .Take(12)
            .Select(x => new OperationalActivityOut
            {
                Id = x.Id,
                LicenseId = x.LicenseId,
                LicenseName = x.License.Name,
                Action = x.Action,
                Status = x.Status,
                Summary = x.Summary,
                UserName = x.UserName,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        var offlineDevices = await context.Devices.AsNoTracking()
            .Where(x => deviceLicenseIds.Contains(x.LicenseId) && !x.IsActive)
            .OrderByDescending(x => x.LastHealthCheckAt)
            .Take(5)
            .Select(x => new { x.LicenseId, LicenseName = x.License.Name, x.Name, x.LastHealthCheckAt })
            .ToListAsync();
        output.Alerts.AddRange(offlineDevices.Select(x => new OperationalAlertOut
        {
            Type = "DeviceOffline",
            Severity = "Error",
            LicenseId = x.LicenseId,
            LicenseName = x.LicenseName,
            Title = $"{x.Name} está offline",
            Message = "O equipamento requer diagnóstico de conectividade.",
            OccurredAt = x.LastHealthCheckAt ?? now,
            TargetUrl = $"/licencas/{x.LicenseId}/equipamentos"
        }));

        var failedBatches = await context.AccessBatchOperations.AsNoTracking()
            .Where(x => credentialLicenseIds.Contains(x.LicenseId) &&
                (x.Status == AccessBatchStatusEnum.Failed ||
                 x.Status == AccessBatchStatusEnum.DeadLetter ||
                 x.Status == AccessBatchStatusEnum.CompletedWithErrors))
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new { x.LicenseId, LicenseName = x.License.Name, x.Status, x.FailedItems, x.CreatedAt })
            .ToListAsync();
        output.Alerts.AddRange(failedBatches.Select(x => new OperationalAlertOut
        {
            Type = "BatchFailure",
            Severity = x.Status == AccessBatchStatusEnum.DeadLetter ? "Error" : "Warning",
            LicenseId = x.LicenseId,
            LicenseName = x.LicenseName,
            Title = "Sincronização requer atenção",
            Message = $"{x.FailedItems} item(ns) não foram sincronizados. Consulte a auditoria da licença para os detalhes.",
            OccurredAt = x.CreatedAt,
            TargetUrl = $"/licencas/{x.LicenseId}/credenciais"
        }));

        var expiredCredentials = await context.ResidentAccessCredentials.AsNoTracking()
            .Where(x => credentialLicenseIds.Contains(x.Resident.Unit.Block.LicenseId) && x.IsActive && x.ValidTo < now)
            .OrderByDescending(x => x.ValidTo)
            .Take(5)
            .Select(x => new
            {
                LicenseId = x.Resident.Unit.Block.LicenseId,
                LicenseName = x.Resident.Unit.Block.License.Name,
                ResidentName = x.Resident.Name,
                x.CredentialType,
                x.ValidTo
            })
            .ToListAsync();
        output.Alerts.AddRange(expiredCredentials.Select(x => new OperationalAlertOut
        {
            Type = "CredentialExpired",
            Severity = "Warning",
            LicenseId = x.LicenseId,
            LicenseName = x.LicenseName,
            Title = $"Credencial vencida de {x.ResidentName}",
            Message = $"{x.CredentialType} está fora da validade configurada.",
            OccurredAt = x.ValidTo,
            TargetUrl = $"/licencas/{x.LicenseId}/credenciais"
        }));

        var pendingBookings = await context.AmenityBookings.AsNoTracking()
            .Where(x => bookingLicenseIds.Contains(x.LicenseId) && x.Status == AmenityBookingStatusEnum.Pending)
            .GroupBy(x => new { x.LicenseId, LicenseName = x.License.Name })
            .Select(group => new
            {
                group.Key.LicenseId,
                group.Key.LicenseName,
                Count = group.Count(),
                Oldest = group.Min(x => x.CreatedAt)
            })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync();
        output.Alerts.AddRange(pendingBookings.Select(x => new OperationalAlertOut
        {
            Type = "BookingApproval",
            Severity = "Info",
            LicenseId = x.LicenseId,
            LicenseName = x.LicenseName,
            Title = "Reservas aguardando aprovação",
            Message = $"{x.Count} solicitação(ões) precisam de análise.",
            OccurredAt = x.Oldest,
            TargetUrl = $"/licencas/{x.LicenseId}/agendamento"
        }));

        output.Alerts = output.Alerts
            .OrderBy(x => x.Severity == "Error" ? 0 : x.Severity == "Warning" ? 1 : 2)
            .ThenByDescending(x => x.OccurredAt)
            .Take(12)
            .ToList();

        return Ok(output);
    }

    [HttpGet("residents/search")]
    public async Task<IActionResult> SearchResidents(
        [FromQuery] string? query,
        [FromQuery] string? document,
        [FromQuery] string? phone,
        [FromQuery] string? credential,
        [FromQuery] string? unit,
        [FromQuery] Guid? licenseId,
        [FromQuery] int take = 50)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId)) return Unauthorized();
        var accessibleLicenseIds = await authorization.GetLicenseIdsWithPermissionAsync(
            User,
            LicensePermissionEnum.ViewPeople,
            HttpContext.RequestAborted);

        var residents = context.Residents
            .AsNoTracking()
            .Where(x => x.Unit.Block.License.EnterpriseId == enterpriseId &&
                accessibleLicenseIds.Contains(x.Unit.Block.LicenseId));

        if (licenseId.HasValue)
            residents = residents.Where(x => x.Unit.Block.LicenseId == licenseId.Value);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = Pattern(query);
            residents = residents.Where(x => EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Email, pattern));
        }
        if (!string.IsNullOrWhiteSpace(document))
        {
            var pattern = Pattern(document);
            residents = residents.Where(x => EF.Functions.ILike(x.CPF, pattern) || EF.Functions.ILike(x.RG, pattern));
        }
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var pattern = Pattern(phone);
            residents = residents.Where(x => EF.Functions.ILike(x.PhoneNumber, pattern));
        }
        if (!string.IsNullOrWhiteSpace(unit))
        {
            var pattern = Pattern(unit);
            residents = residents.Where(x => EF.Functions.ILike(x.Unit.Number, pattern) || EF.Functions.ILike(x.Unit.Block.Name, pattern));
        }
        if (!string.IsNullOrWhiteSpace(credential))
        {
            var pattern = Pattern(credential);
            residents = residents.Where(x => x.AccessCredentials.Any(c => EF.Functions.ILike(c.Identifier, pattern)));
        }

        var result = await residents
            .OrderBy(x => x.Name)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => new GlobalResidentSearchOut
            {
                Id = x.Id,
                LicenseId = x.Unit.Block.LicenseId,
                LicenseName = x.Unit.Block.License.Name,
                Name = x.Name,
                BlockName = x.Unit.Block.Name,
                UnitNumber = x.Unit.Number,
                CPF = x.CPF,
                RG = x.RG,
                PhoneNumber = x.PhoneNumber,
                Email = x.Email,
                AccessType = x.AccessType.ToString(),
                Temporary = x.Temporary,
                Expire = x.Expire,
                Credentials = x.AccessCredentials
                    .OrderByDescending(c => c.IsActive)
                    .ThenBy(c => c.CredentialType)
                    .Take(5)
                    .Select(c => new GlobalCredentialSearchOut
                    {
                        Type = c.CredentialType.ToString(),
                        Identifier = c.CredentialType == AccessCredentialTypeEnum.Password ? "********" : c.Identifier,
                        IsActive = c.IsActive
                    })
                    .ToList()
            })
            .ToListAsync();

        return Ok(result);
    }

    private static string Pattern(string value) => $"%{value.Trim()}%";

    private static HashSet<Guid> PermissionScope(
        IReadOnlyDictionary<Guid, LicensePermissionEnum> permissions,
        LicensePermissionEnum required) =>
        permissions
            .Where(x => (x.Value & required) == required)
            .Select(x => x.Key)
            .ToHashSet();
}
