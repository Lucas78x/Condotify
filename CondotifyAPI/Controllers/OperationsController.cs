using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Domain.DTO.Observability;
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

        var alertLicenseIds = PermissionScope(permissionMap, LicensePermissionEnum.ViewAlerts);
        output.Alerts = await context.OperationalAlerts.AsNoTracking()
            .Where(x => x.LicenseId != null &&
                        alertLicenseIds.Contains(x.LicenseId.Value) &&
                        x.Status != OperationalAlertStatus.Resolved)
            .OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.LastOccurredAt)
            .Take(12)
            .Select(x => new OperationalAlertOut
            {
                Id = x.Id,
                Type = x.Type,
                Severity = x.Severity.ToString(),
                Status = x.Status.ToString(),
                LicenseId = x.LicenseId,
                LicenseName = x.License!.Name,
                Title = x.Title,
                Message = x.Message,
                IsConditionActive = x.IsConditionActive,
                OccurrenceCount = x.OccurrenceCount,
                FirstOccurredAt = x.FirstOccurredAt,
                OccurredAt = x.LastOccurredAt,
                AcknowledgedAt = x.AcknowledgedAt,
                AcknowledgedBy = x.AcknowledgedBy,
                AcknowledgementNote = x.AcknowledgementNote,
                ResolvedAt = x.ResolvedAt,
                ResolvedBy = x.ResolvedBy,
                ResolutionNote = x.ResolutionNote,
                TargetUrl = x.TargetUrl,
                SuppressedUntil = x.SuppressedUntil,
                SuppressedBy = x.SuppressedBy,
                SuppressionReason = x.SuppressionReason
            })
            .ToListAsync();

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
                UnitId = x.UnitId,
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
