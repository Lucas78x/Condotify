using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/offline")]
public sealed class OfflineOperationsController(
    DatabaseContext context,
    IOfflineAccessBundleService bundles,
    IHubContext<CondotifyAPI.Hubs.ConciergeHub> hub,
    IPlatformPushNotifier? push = null) : ControllerBase
{
    [HttpPost("devices/register")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> RegisterDevice(
        Guid licenseId,
        [FromBody] OfflineDeviceRegistrationViewModel input,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRegistration(input);
        if (validation is not null) return BadRequest(new { Errors = validation });
        if (!TryCurrentUser(out var userId)) return Forbid();

        var now = DateTime.UtcNow;
        var device = await RegisterCoreAsync(licenseId, userId, input, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(await ToDeviceAsync(device, includeSecret: device.Status == OfflineDeviceStatusEnum.Approved, cancellationToken));
    }

    [HttpPost("sync")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> Sync(
        Guid licenseId,
        [FromBody] OfflineSyncRequestViewModel input,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRegistration(input);
        if (validation is not null) return BadRequest(new { Errors = validation });
        if (input.Operations.Count > 100)
            return BadRequest(new { Errors = "Envie no máximo 100 operações por sincronização." });
        if (input.Operations.Any(x => x.ClientOperationId == Guid.Empty) ||
            input.Operations.Select(x => x.ClientOperationId).Distinct().Count() != input.Operations.Count)
            return BadRequest(new { Errors = "A fila contém identificadores de operação inválidos ou repetidos." });
        if (!TryCurrentUser(out var userId)) return Forbid();

        var now = DateTime.UtcNow;
        var device = await context.OfflineAccessDevices.Include(x => x.User)
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.InstallationId == input.InstallationId.Trim(), cancellationToken)
            ?? await RegisterCoreAsync(licenseId, userId, input, now, cancellationToken);

        if (device.UserId != userId)
        {
            ResetOwnership(device, userId, input, now);
            Audit(licenseId, device.Id, "OfflineDeviceOwnershipChanged", "Pending",
                $"O aparelho {device.DeviceName} foi associado a outro usuário e exige nova aprovação.",
                new { device.InstallationId });
        }

        device.DeviceName = input.DeviceName.Trim();
        device.Platform = input.Platform.Trim();
        device.AppVersion = input.AppVersion.Trim();
        device.LastSeenAt = now;
        device.UpdatedAt = now;

        if (device.Status != OfflineDeviceStatusEnum.Approved)
        {
            await context.SaveChangesAsync(cancellationToken);
            return Ok(new OfflineSyncResultViewModel
            {
                Device = await ToDeviceAsync(device, includeSecret: false, cancellationToken),
                ServerTime = now
            });
        }

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var results = new List<OfflineOperationResultViewModel>(input.Operations.Count);
        var appliedVisits = new List<AccessVisitDTO>();
        foreach (var operation in input.Operations.OrderBy(x => x.OccurredAt))
        {
            var processed = await ProcessOperationAsync(device, operation, userId, CurrentActor(), now, cancellationToken);
            results.Add(ToOperation(processed));
            if (processed.Status == OfflineOperationStatusEnum.Applied && processed.Visit is not null)
                appliedVisits.Add(processed.Visit);
        }

        await context.SaveChangesAsync(cancellationToken);
        var built = await bundles.BuildAsync(device, cancellationToken);
        device.LastSyncedAt = now;
        device.LastSeenAt = now;
        device.LastBundleId = built.Payload.BundleId;
        device.LastBundleGeneratedAt = built.Payload.GeneratedAt;
        device.LastBundleExpiresAt = built.Payload.ExpiresAt;
        device.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        foreach (var visit in appliedVisits.GroupBy(x => x.Id).Select(x => x.First()))
        {
            await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId))
                .SendAsync("VisitStatusChanged", new { visit.Id, Status = visit.Status.ToString() }, cancellationToken);
            if (visit.Status == AccessVisitStatusEnum.CheckedIn && push is not null)
                await push.NotifyResidentAsync(
                    visit.HostResidentId,
                    CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory.Visitor,
                    "Visitante na portaria",
                    $"A entrada de {visit.VisitorName} foi registrada após operação offline.",
                    $"/visitors/{visit.Id:D}",
                    $"visitor-offline:{visit.Id:N}:{results.FirstOrDefault(x => x.VisitId == visit.Id)?.ClientOperationId:N}",
                    cancellationToken);
        }

        return Ok(new OfflineSyncResultViewModel
        {
            Device = await ToDeviceAsync(device, includeSecret: true, cancellationToken),
            Bundle = built.Envelope,
            Operations = results,
            ServerTime = DateTime.UtcNow
        });
    }

    [HttpGet("devices")]
    [RequireLicensePermission(LicensePermissionEnum.ViewSettings)]
    public async Task<IActionResult> Devices(Guid licenseId, CancellationToken cancellationToken)
    {
        var devices = await context.OfflineAccessDevices.AsNoTracking().Include(x => x.User)
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Status).ThenByDescending(x => x.LastSyncedAt ?? x.CreatedAt)
            .ToListAsync(cancellationToken);
        var output = new List<OfflineDeviceViewModel>(devices.Count);
        foreach (var device in devices)
            output.Add(await ToDeviceAsync(device, includeSecret: false, cancellationToken));
        return Ok(output);
    }

    [HttpPatch("devices/{deviceId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageSettings)]
    public async Task<IActionResult> UpdateDevice(
        Guid licenseId,
        Guid deviceId,
        [FromBody] OfflineDevicePolicyViewModel input,
        CancellationToken cancellationToken)
    {
        if (input.OfflineWindowMinutes is < 15 or > 720)
            return BadRequest(new { Errors = "A janela offline deve ficar entre 15 minutos e 12 horas." });
        var device = await context.OfflineAccessDevices.Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);
        if (device is null) return NotFound();

        var now = DateTime.UtcNow;
        var actor = CurrentActor();
        var previousStatus = device.Status;
        if (input.Status == OfflineDeviceStatus.Approved)
        {
            if (previousStatus == OfflineDeviceStatusEnum.Revoked)
                device.DeviceSecret = NewSecret();
            device.Status = OfflineDeviceStatusEnum.Approved;
            device.ApprovedAt = now;
            device.ApprovedBy = actor;
            device.RevokedAt = null;
            device.RevokedBy = string.Empty;
            var hasPrimary = await context.OfflineAccessDevices.AnyAsync(x =>
                x.LicenseId == licenseId && x.Id != device.Id && x.Status == OfflineDeviceStatusEnum.Approved && x.IsPrimaryValidator,
                cancellationToken);
            device.IsPrimaryValidator = input.IsPrimaryValidator || !hasPrimary;
            if (device.IsPrimaryValidator)
            {
                var otherPrimaries = await context.OfflineAccessDevices
                    .Where(x => x.LicenseId == licenseId && x.Id != device.Id && x.IsPrimaryValidator)
                    .ToListAsync(cancellationToken);
                foreach (var item in otherPrimaries) { item.IsPrimaryValidator = false; item.UpdatedAt = now; }
            }
        }
        else if (input.Status == OfflineDeviceStatus.Revoked)
        {
            device.Status = OfflineDeviceStatusEnum.Revoked;
            device.IsPrimaryValidator = false;
            device.RevokedAt = now;
            device.RevokedBy = actor;
            device.LastBundleExpiresAt = now;
        }
        else
        {
            device.Status = OfflineDeviceStatusEnum.Pending;
            device.IsPrimaryValidator = false;
            device.ApprovedAt = null;
            device.ApprovedBy = string.Empty;
        }

        device.OfflineWindowMinutes = input.OfflineWindowMinutes;
        device.UpdatedAt = now;
        Audit(licenseId, device.Id, "OfflineDevicePolicyChanged", device.Status.ToString(),
            $"A política offline de {device.DeviceName} foi atualizada.",
            new { PreviousStatus = previousStatus.ToString(), Status = device.Status.ToString(), device.OfflineWindowMinutes, device.IsPrimaryValidator });
        await context.SaveChangesAsync(cancellationToken);
        return Ok(await ToDeviceAsync(device, includeSecret: false, cancellationToken));
    }

    [HttpGet("operations")]
    [RequireLicensePermission(LicensePermissionEnum.ViewSettings)]
    public async Task<IActionResult> Operations(
        Guid licenseId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);
        var query = context.OfflineAccessOperations.AsNoTracking()
            .Include(x => x.Device).Include(x => x.Visit)
            .Where(x => x.LicenseId == licenseId);
        var page = new OfflineOperationPageViewModel
        {
            Total = await query.CountAsync(cancellationToken),
            Applied = await query.CountAsync(x => x.Status == OfflineOperationStatusEnum.Applied || x.Status == OfflineOperationStatusEnum.Duplicate, cancellationToken),
            Conflicts = await query.CountAsync(x => x.Status == OfflineOperationStatusEnum.Conflict, cancellationToken),
            Rejected = await query.CountAsync(x => x.Status == OfflineOperationStatusEnum.Rejected, cancellationToken),
            Items = (await query.OrderByDescending(x => x.ReceivedAt).Skip(skip).Take(take).ToListAsync(cancellationToken))
                .Select(ToOperation).ToList()
        };
        return Ok(page);
    }

    private async Task<OfflineAccessDeviceDTO> RegisterCoreAsync(
        Guid licenseId,
        Guid userId,
        OfflineDeviceRegistrationViewModel input,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var installationId = input.InstallationId.Trim();
        var device = await context.OfflineAccessDevices.Include(x => x.User)
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.InstallationId == installationId, cancellationToken);
        if (device is null)
        {
            device = new OfflineAccessDeviceDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, UserId = userId, InstallationId = installationId,
                DeviceName = input.DeviceName.Trim(), Platform = input.Platform.Trim(), AppVersion = input.AppVersion.Trim(),
                Status = OfflineDeviceStatusEnum.Pending, DeviceSecret = NewSecret(), OfflineWindowMinutes = 480,
                LastSeenAt = now, CreatedAt = now, UpdatedAt = now
            };
            context.OfflineAccessDevices.Add(device);
            Audit(licenseId, device.Id, "OfflineDeviceRegistered", "Pending",
                $"O aparelho {device.DeviceName} solicitou autorização para operação offline.",
                new { device.InstallationId, device.Platform, device.AppVersion });
        }
        else if (device.UserId != userId)
        {
            ResetOwnership(device, userId, input, now);
        }
        else
        {
            device.DeviceName = input.DeviceName.Trim();
            device.Platform = input.Platform.Trim();
            device.AppVersion = input.AppVersion.Trim();
            device.LastSeenAt = now;
            device.UpdatedAt = now;
        }

        return device;
    }

    private async Task<OfflineAccessOperationDTO> ProcessOperationAsync(
        OfflineAccessDeviceDTO device,
        OfflineOperationUploadViewModel input,
        Guid userId,
        string userName,
        DateTime receivedAt,
        CancellationToken cancellationToken)
    {
        var existing = await context.OfflineAccessOperations.Include(x => x.Visit)
            .FirstOrDefaultAsync(x => x.DeviceId == device.Id && x.ClientOperationId == input.ClientOperationId, cancellationToken);
        if (existing is not null) return existing;

        var occurredAt = Utc(input.OccurredAt);
        var visit = await context.AccessVisits.Include(x => x.Credential).Include(x => x.HostResident)
            .FirstOrDefaultAsync(x => x.Id == input.VisitId && x.LicenseId == device.LicenseId, cancellationToken);
        var row = new OfflineAccessOperationDTO
        {
            Id = Guid.NewGuid(), LicenseId = device.LicenseId, DeviceId = device.Id,
            VisitId = visit?.Id, Visit = visit, ClientOperationId = input.ClientOperationId, BundleId = input.BundleId,
            Kind = (OfflineOperationKindEnum)(int)input.Kind, Status = OfflineOperationStatusEnum.Pending,
            CodeHash = input.CodeHash.Trim().ToUpperInvariant(), UserId = userId, UserName = userName,
            OccurredAt = occurredAt, ReceivedAt = receivedAt,
            BeforeStatus = visit?.Status.ToString() ?? string.Empty
        };
        context.OfflineAccessOperations.Add(row);

        if (visit is null) return Reject(row, "A visita informada não existe neste condomínio.");
        if (input.BundleId == Guid.Empty || device.LastBundleId != input.BundleId ||
            !device.LastBundleGeneratedAt.HasValue || !device.LastBundleExpiresAt.HasValue)
            return Reject(row, "O pacote operacional não corresponde à última sincronização deste aparelho.");
        if (occurredAt < device.LastBundleGeneratedAt.Value.AddMinutes(-5) ||
            occurredAt > device.LastBundleExpiresAt.Value || occurredAt > receivedAt.AddMinutes(5))
            return Reject(row, "O registro está fora da janela confiável do pacote offline.");
        if (row.CodeHash.Length != 64 || !row.CodeHash.Equals(OfflineAccessCode.Hash(visit.Credential.Identifier), StringComparison.Ordinal))
            return Reject(row, "O QR Code não corresponde à autorização sincronizada.");
        if (row.Kind == OfflineOperationKindEnum.VisitCheckIn)
        {
            if (!visit.Credential.IsActive || occurredAt < visit.ValidFrom || occurredAt > visit.ValidTo)
                return Reject(row, "A autorização estava inativa ou fora do período permitido.");
            if (visit.Credential.MaxUses.HasValue && visit.Credential.UseCount >= visit.Credential.MaxUses.Value)
                return Reject(row, "O limite de utilizações desta autorização foi atingido.");
            if (visit.Credential.MaxUses == 1 && !device.IsPrimaryValidator)
                return Reject(row, "Autorizações de uso único exigem o validador offline principal.");
            var routes = await bundles.ResolveRouteWindowsAsync(device.LicenseId, new[] { visit.GuestResidentId }, cancellationToken);
            if (!OfflineRouteSchedule.IsAllowed(routes.GetValueOrDefault(visit.GuestResidentId) ?? [], occurredAt, OfflineOperationsTimeZone.OffsetMinutes(occurredAt)))
                return Reject(row, "A autorização estava fora dos dias ou horários permitidos pela rota.");
            if (visit.Status == AccessVisitStatusEnum.CheckedIn)
                return Conflict(row, "A entrada já havia sido registrada por outra origem.");
            if (visit.Status != AccessVisitStatusEnum.Scheduled)
                return Reject(row, $"Uma visita com situação {visit.Status} não permite registrar entrada.");
            visit.Status = AccessVisitStatusEnum.CheckedIn;
            visit.CheckedInAt = occurredAt;
            visit.UpdatedAt = receivedAt;
            visit.Credential.UseCount++;
            visit.Credential.UpdatedAt = receivedAt;
            return Apply(row, visit.Status.ToString(), "Entrada offline reconciliada com sucesso.");
        }

        if (visit.Status == AccessVisitStatusEnum.CheckedOut)
            return Conflict(row, "A saída já havia sido registrada por outra origem.");
        if (visit.Status != AccessVisitStatusEnum.CheckedIn)
            return Reject(row, $"Uma visita com situação {visit.Status} não permite registrar saída.");
        visit.Status = AccessVisitStatusEnum.CheckedOut;
        visit.CheckedOutAt = occurredAt;
        visit.UpdatedAt = receivedAt;
        visit.Credential.IsActive = false;
        visit.Credential.UpdatedAt = receivedAt;
        return Apply(row, visit.Status.ToString(), "Saída offline reconciliada com sucesso.");
    }

    private OfflineAccessOperationDTO Apply(OfflineAccessOperationDTO row, string after, string message) =>
        Finish(row, OfflineOperationStatusEnum.Applied, after, message);

    private OfflineAccessOperationDTO Reject(OfflineAccessOperationDTO row, string message) =>
        Finish(row, OfflineOperationStatusEnum.Rejected, row.BeforeStatus, message);

    private OfflineAccessOperationDTO Conflict(OfflineAccessOperationDTO row, string message) =>
        Finish(row, OfflineOperationStatusEnum.Conflict, row.BeforeStatus, message);

    private OfflineAccessOperationDTO Finish(
        OfflineAccessOperationDTO row,
        OfflineOperationStatusEnum status,
        string after,
        string message)
    {
        row.Status = status;
        row.AfterStatus = after;
        row.Message = message;
        Audit(row.LicenseId, row.VisitId, "OfflineVisitOperation", status.ToString(), message,
            new { row.ClientOperationId, row.BundleId, row.DeviceId, Kind = row.Kind.ToString() });
        return row;
    }

    private async Task<OfflineDeviceViewModel> ToDeviceAsync(
        OfflineAccessDeviceDTO item,
        bool includeSecret,
        CancellationToken cancellationToken)
    {
        var counts = await context.OfflineAccessOperations.AsNoTracking()
            .Where(x => x.DeviceId == item.Id)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Pending = group.Count(x => x.Status == OfflineOperationStatusEnum.Pending),
                Conflicts = group.Count(x => x.Status == OfflineOperationStatusEnum.Conflict || x.Status == OfflineOperationStatusEnum.Rejected)
            }).FirstOrDefaultAsync(cancellationToken);
        return new OfflineDeviceViewModel
        {
            Id = item.Id, LicenseId = item.LicenseId, UserId = item.UserId,
            UserName = item.User is not null && item.User.Id == item.UserId ? item.User.Name : includeSecret ? CurrentActor() : string.Empty,
            InstallationId = item.InstallationId,
            DeviceName = item.DeviceName, Platform = item.Platform, AppVersion = item.AppVersion,
            Status = (OfflineDeviceStatus)(int)item.Status, OfflineWindowMinutes = item.OfflineWindowMinutes,
            IsPrimaryValidator = item.IsPrimaryValidator, LastSeenAt = item.LastSeenAt, LastSyncedAt = item.LastSyncedAt,
            LastBundleExpiresAt = item.LastBundleExpiresAt, CreatedAt = item.CreatedAt, UpdatedAt = item.UpdatedAt,
            ApprovedBy = item.ApprovedBy, ApprovedAt = item.ApprovedAt, RevokedBy = item.RevokedBy, RevokedAt = item.RevokedAt,
            PendingOperations = counts?.Pending ?? 0, ConflictOperations = counts?.Conflicts ?? 0,
            DeviceSecret = includeSecret ? item.DeviceSecret : string.Empty
        };
    }

    private static OfflineOperationResultViewModel ToOperation(OfflineAccessOperationDTO item) => new()
    {
        Id = item.Id, ClientOperationId = item.ClientOperationId, DeviceId = item.DeviceId,
        VisitId = item.VisitId ?? Guid.Empty, VisitorName = item.Visit?.VisitorName ?? string.Empty,
        Kind = (OfflineOperationKind)(int)item.Kind, Status = (OfflineOperationStatus)(int)item.Status,
        Message = item.Message, OccurredAt = item.OccurredAt, ReceivedAt = item.ReceivedAt
    };

    private static void ResetOwnership(
        OfflineAccessDeviceDTO device,
        Guid userId,
        OfflineDeviceRegistrationViewModel input,
        DateTime now)
    {
        device.UserId = userId;
        device.DeviceName = input.DeviceName.Trim();
        device.Platform = input.Platform.Trim();
        device.AppVersion = input.AppVersion.Trim();
        device.Status = OfflineDeviceStatusEnum.Pending;
        device.DeviceSecret = NewSecret();
        device.IsPrimaryValidator = false;
        device.LastBundleId = null;
        device.LastBundleGeneratedAt = null;
        device.LastBundleExpiresAt = null;
        device.LastSyncedAt = null;
        device.ApprovedAt = null;
        device.ApprovedBy = string.Empty;
        device.RevokedAt = null;
        device.RevokedBy = string.Empty;
        device.LastSeenAt = now;
        device.UpdatedAt = now;
    }

    private void Audit(Guid licenseId, Guid? entityId, string action, string status, string summary, object details) =>
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "OfflineOperation", EntityId = entityId,
            Action = action, Status = status, Summary = summary, DetailsJson = JsonSerializer.Serialize(details),
            UserId = TryCurrentUser(out var userId) ? userId : null, UserName = CurrentActor(), CreatedAt = DateTime.UtcNow
        });

    private bool TryCurrentUser(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private string CurrentActor() => User.FindFirstValue("name") ?? User.Identity?.Name ?? "Operação";

    private static string? ValidateRegistration(OfflineDeviceRegistrationViewModel input)
    {
        if (string.IsNullOrWhiteSpace(input.InstallationId) || input.InstallationId.Trim().Length > 100)
            return "A identificação da instalação é inválida.";
        if (string.IsNullOrWhiteSpace(input.DeviceName) || input.DeviceName.Trim().Length > 160)
            return "Informe um nome válido para o aparelho.";
        if (input.Platform.Trim().Length > 60 || input.AppVersion.Trim().Length > 40)
            return "As informações do aplicativo são inválidas.";
        return null;
    }

    private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
