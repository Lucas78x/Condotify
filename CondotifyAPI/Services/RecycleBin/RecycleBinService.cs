using System.Text.Json;
using System.Text.Json.Serialization;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.Location;
using CondotifyAPI.Domain.DTO.RecycleBin;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.RecycleBin;

public interface IRecycleBinService
{
    RecycleBinItemDTO CaptureBlock(Guid licenseId, BlockDTO block, string actor);
    RecycleBinItemDTO CaptureUnit(Guid licenseId, UnitDTO unit, string actor);
    RecycleBinItemDTO CaptureResident(
        Guid licenseId,
        ResidentAccessDTO resident,
        ResidentUnitLinkDTO? link,
        IReadOnlyCollection<RegistrationInviteDTO> invites,
        IReadOnlyCollection<AccessRouteResidentOverrideDTO> overrides,
        string actor);
    RecycleBinItemDTO CaptureVehicle(Guid licenseId, VehicleDTO vehicle, string actor);
    RecycleBinItemDTO CaptureDevice(Guid licenseId, AccessControlDeviceDTO device, string actor);
    Task<RecycleRestoreResult> RestoreAsync(Guid licenseId, Guid itemId, string actor, CancellationToken cancellationToken);
    Task<RecyclePurgeResult> PurgeAsync(Guid licenseId, Guid itemId, CancellationToken cancellationToken);
}

public sealed class RecycleBinService(
    DatabaseContext context,
    IPrivateMediaStore media) : IRecycleBinService
{
    public const int RetentionDays = 30;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public RecycleBinItemDTO CaptureBlock(Guid licenseId, BlockDTO block, string actor) =>
        Capture(licenseId, "Block", block.Id, block.Name,
            new BlockSnapshot(block.Name, block.CreatedAt, block.LastUpdatedAt), actor);

    public RecycleBinItemDTO CaptureUnit(Guid licenseId, UnitDTO unit, string actor) =>
        Capture(licenseId, "Unit", unit.Id, unit.Number,
            new UnitSnapshot(unit.BlockId, unit.Number, unit.Floor), actor);

    public RecycleBinItemDTO CaptureResident(
        Guid licenseId,
        ResidentAccessDTO resident,
        ResidentUnitLinkDTO? link,
        IReadOnlyCollection<RegistrationInviteDTO> invites,
        IReadOnlyCollection<AccessRouteResidentOverrideDTO> overrides,
        string actor) =>
        Capture(licenseId, "Resident", resident.Id, resident.Name,
            new ResidentSnapshot(
                resident.UnitId,
                resident.Name,
                resident.Email,
                resident.Password,
                resident.PhoneNumber,
                resident.CommercialPhone,
                resident.CPF,
                resident.RG,
                resident.BirthDate,
                resident.ApartmentNumber,
                resident.ImgUrl,
                resident.Description,
                resident.AccessType,
                resident.FirstAccess,
                resident.NotifyAccess,
                resident.IsActive,
                resident.Temporary,
                resident.Expire,
                resident.LastAccess,
                resident.CreatedAt,
                link?.Relationship ?? ResidentUnitRelationshipEnum.Resident,
                link?.Description ?? string.Empty,
                invites.Select(InvitationSnapshot.From).ToList(),
                overrides.Select(RouteOverrideSnapshot.From).ToList()),
            actor);

    public RecycleBinItemDTO CaptureVehicle(Guid licenseId, VehicleDTO vehicle, string actor) =>
        Capture(licenseId, "Vehicle", vehicle.Id, vehicle.Plate,
            new VehicleSnapshot(
                vehicle.UnitId,
                vehicle.ResidentId,
                vehicle.Plate,
                vehicle.Brand,
                vehicle.Model,
                vehicle.Color,
                vehicle.Type,
                vehicle.TagIdentifier,
                vehicle.IsActive,
                vehicle.CreatedAt,
                vehicle.UpdatedAt),
            actor);

    public RecycleBinItemDTO CaptureDevice(Guid licenseId, AccessControlDeviceDTO device, string actor) =>
        Capture(licenseId, "Device", device.Id, device.Name,
            new DeviceSnapshot(
                device.Name,
                device.IPAddress,
                device.Port,
                device.Username,
                device.Password,
                device.MACAddress,
                device.Model,
                device.SerialNumber,
                device.FirmwareVersion,
                device.Type,
                device.IsActive,
                device.Location.X,
                device.Location.Y,
                device.CreatedAt,
                device.LastUpdatedAt,
                device.LastHealthCheckAt,
                device.LastSeenAt,
                device.LastResponseTimeMs,
                device.HealthMessage,
                device.CapacityJson,
                device.DiscoveredPortalsJson),
            actor);

    public async Task<RecycleRestoreResult> RestoreAsync(
        Guid licenseId,
        Guid itemId,
        string actor,
        CancellationToken cancellationToken)
    {
        var item = await context.RecycleBinItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.LicenseId == licenseId, cancellationToken);
        if (item is null) return RecycleRestoreResult.NotFound();
        if (item.RestoredAt.HasValue) return RecycleRestoreResult.Conflict("Este item ja foi restaurado.");
        if (item.ExpiresAt <= DateTime.UtcNow) return RecycleRestoreResult.Conflict("O prazo de restauracao deste item expirou.");

        var result = item.EntityType switch
        {
            "Block" => await RestoreBlockAsync(item, cancellationToken),
            "Unit" => await RestoreUnitAsync(item, cancellationToken),
            "Resident" => await RestoreResidentAsync(item, cancellationToken),
            "Vehicle" => await RestoreVehicleAsync(item, cancellationToken),
            "Device" => await RestoreDeviceAsync(item, cancellationToken),
            _ => RecycleRestoreResult.Conflict("O tipo deste item nao pode ser restaurado.")
        };
        if (!result.Success) return result;

        var now = DateTime.UtcNow;
        item.RestoredAt = now;
        item.RestoredBy = actor;
        item.SnapshotJson = "{}";
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            Action = "Restored",
            Status = "Success",
            Summary = $"{item.DisplayName} restaurado da lixeira.",
            DetailsJson = JsonSerializer.Serialize(new { RecycleBinItemId = item.Id }, JsonOptions),
            UserName = actor,
            CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        return result with { ItemId = item.Id, EntityId = item.EntityId, EntityType = item.EntityType };
    }

    public async Task<RecyclePurgeResult> PurgeAsync(
        Guid licenseId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await context.RecycleBinItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.LicenseId == licenseId, cancellationToken);
        if (item is null) return new(false, false);

        string? residentPhoto = null;
        if (!item.RestoredAt.HasValue && item.EntityType == "Resident")
        {
            var snapshot = Deserialize<ResidentSnapshot>(item.SnapshotJson);
            residentPhoto = snapshot?.ImageUrl;
        }

        context.RecycleBinItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(residentPhoto))
            await media.DeleteAsync(licenseId, residentPhoto, cancellationToken);
        return new(true, true);
    }

    private RecycleBinItemDTO Capture<T>(
        Guid licenseId,
        string entityType,
        Guid entityId,
        string displayName,
        T snapshot,
        string actor)
    {
        var now = DateTime.UtcNow;
        var item = new RecycleBinItemDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = entityType,
            EntityId = entityId,
            DisplayName = displayName,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions),
            DeletedBy = actor,
            DeletedAt = now,
            ExpiresAt = now.AddDays(RetentionDays)
        };
        context.RecycleBinItems.Add(item);
        return item;
    }

    private async Task<RecycleRestoreResult> RestoreBlockAsync(RecycleBinItemDTO item, CancellationToken cancellationToken)
    {
        var snapshot = Deserialize<BlockSnapshot>(item.SnapshotJson);
        if (snapshot is null) return RecycleRestoreResult.Conflict("O snapshot do agrupamento esta corrompido.");
        if (await context.Blocks.AnyAsync(x => x.LicenseId == item.LicenseId && EF.Functions.ILike(x.Name, snapshot.Name), cancellationToken))
            return RecycleRestoreResult.Conflict("Ja existe um agrupamento com este nome.");

        context.Blocks.Add(new BlockDTO
        {
            Id = item.EntityId,
            LicenseId = item.LicenseId,
            Name = snapshot.Name,
            CreatedAt = snapshot.CreatedAt,
            LastUpdatedAt = DateTime.UtcNow
        });
        return RecycleRestoreResult.Ok("Agrupamento restaurado.");
    }

    private async Task<RecycleRestoreResult> RestoreUnitAsync(RecycleBinItemDTO item, CancellationToken cancellationToken)
    {
        var snapshot = Deserialize<UnitSnapshot>(item.SnapshotJson);
        if (snapshot is null) return RecycleRestoreResult.Conflict("O snapshot da unidade esta corrompido.");
        var blockExists = await context.Blocks.AnyAsync(x => x.Id == snapshot.BlockId && x.LicenseId == item.LicenseId, cancellationToken);
        if (!blockExists) return RecycleRestoreResult.Conflict("Restaure primeiro o agrupamento original desta unidade.");
        if (await context.Units.AnyAsync(x => x.BlockId == snapshot.BlockId && EF.Functions.ILike(x.Number, snapshot.Number), cancellationToken))
            return RecycleRestoreResult.Conflict("Ja existe uma unidade com este numero no agrupamento.");

        context.Units.Add(new UnitDTO
        {
            Id = item.EntityId,
            BlockId = snapshot.BlockId,
            Number = snapshot.Number,
            Floor = snapshot.Floor
        });
        return RecycleRestoreResult.Ok("Unidade restaurada.");
    }

    private async Task<RecycleRestoreResult> RestoreResidentAsync(RecycleBinItemDTO item, CancellationToken cancellationToken)
    {
        var snapshot = Deserialize<ResidentSnapshot>(item.SnapshotJson);
        if (snapshot is null) return RecycleRestoreResult.Conflict("O snapshot da pessoa esta corrompido.");
        var unitExists = await context.Units.AnyAsync(x =>
            x.Id == snapshot.UnitId && x.Block.LicenseId == item.LicenseId, cancellationToken);
        if (!unitExists) return RecycleRestoreResult.Conflict("Restaure primeiro a unidade original desta pessoa.");
        if (!string.IsNullOrWhiteSpace(snapshot.CPF) && await context.Residents.AnyAsync(x =>
                x.UnitId == snapshot.UnitId && x.CPF == snapshot.CPF, cancellationToken))
            return RecycleRestoreResult.Conflict("Ja existe uma pessoa com este CPF na unidade.");

        var missingRoute = snapshot.RouteOverrides
            .Select(x => x.AccessRouteId)
            .Except(await context.AccessRoutes.Where(x => x.LicenseId == item.LicenseId).Select(x => x.Id).ToListAsync(cancellationToken))
            .Any();
        if (missingRoute) return RecycleRestoreResult.Conflict("Uma rota vinculada a esta pessoa nao existe mais.");

        var resident = new ResidentAccessDTO
        {
            Id = item.EntityId,
            UnitId = snapshot.UnitId,
            Name = snapshot.Name,
            Email = snapshot.Email,
            Password = snapshot.Password,
            PhoneNumber = snapshot.PhoneNumber,
            CommercialPhone = snapshot.CommercialPhone,
            CPF = snapshot.CPF,
            RG = snapshot.RG,
            BirthDate = snapshot.BirthDate,
            ApartmentNumber = snapshot.ApartmentNumber,
            ImgUrl = snapshot.ImageUrl,
            Description = snapshot.Description,
            AccessType = snapshot.AccessType,
            FirstAccess = snapshot.FirstAccess,
            NotifyAccess = snapshot.NotifyAccess,
            IsActive = snapshot.IsActive,
            Temporary = snapshot.Temporary,
            Expire = snapshot.Expire,
            LastAccess = snapshot.LastAccess,
            CreatedAt = snapshot.CreatedAt
        };
        context.Residents.Add(resident);
        var now = DateTime.UtcNow;
        context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
        {
            Id = Guid.NewGuid(),
            ResidentId = resident.Id,
            UnitId = snapshot.UnitId,
            Relationship = snapshot.Relationship,
            Description = snapshot.LinkDescription,
            IsPrimary = true,
            IsActive = true,
            StartsAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        foreach (var invite in snapshot.Invitations)
            context.RegistrationInvites.Add(invite.ToDto(item.LicenseId, resident.Id));
        foreach (var routeOverride in snapshot.RouteOverrides)
            context.AccessRouteResidentOverrides.Add(routeOverride.ToDto(resident.Id));
        return RecycleRestoreResult.Ok("Pessoa restaurada.");
    }

    private async Task<RecycleRestoreResult> RestoreVehicleAsync(RecycleBinItemDTO item, CancellationToken cancellationToken)
    {
        var snapshot = Deserialize<VehicleSnapshot>(item.SnapshotJson);
        if (snapshot is null) return RecycleRestoreResult.Conflict("O snapshot do veiculo esta corrompido.");
        var unitExists = await context.Units.AnyAsync(x =>
            x.Id == snapshot.UnitId && x.Block.LicenseId == item.LicenseId, cancellationToken);
        if (!unitExists) return RecycleRestoreResult.Conflict("Restaure primeiro a unidade original deste veiculo.");
        if (snapshot.ResidentId.HasValue && !await context.Residents.AnyAsync(x =>
                x.Id == snapshot.ResidentId && x.Unit.Block.LicenseId == item.LicenseId, cancellationToken))
            return RecycleRestoreResult.Conflict("Restaure primeiro a pessoa vinculada ao veiculo.");
        if (await context.Vehicles.AnyAsync(x => x.UnitId == snapshot.UnitId && x.Plate == snapshot.Plate, cancellationToken))
            return RecycleRestoreResult.Conflict("Ja existe um veiculo com esta placa na unidade.");
        if (snapshot.ResidentId.HasValue && !string.IsNullOrWhiteSpace(snapshot.TagIdentifier))
        {
            var duplicateTag = await context.ResidentAccessCredentials.AnyAsync(x =>
                x.ResidentId == snapshot.ResidentId &&
                x.CredentialType == AccessCredentialTypeEnum.VehicleTag &&
                x.Identifier == snapshot.TagIdentifier, cancellationToken);
            if (duplicateTag) return RecycleRestoreResult.Conflict("A TAG ja esta vinculada a outra credencial da pessoa.");
        }

        context.Vehicles.Add(new VehicleDTO
        {
            Id = item.EntityId,
            UnitId = snapshot.UnitId,
            ResidentId = snapshot.ResidentId,
            Plate = snapshot.Plate,
            Brand = snapshot.Brand,
            Model = snapshot.Model,
            Color = snapshot.Color,
            Type = snapshot.Type,
            TagIdentifier = snapshot.TagIdentifier,
            IsActive = snapshot.IsActive,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        });
        if (snapshot.ResidentId.HasValue && !string.IsNullOrWhiteSpace(snapshot.TagIdentifier))
        {
            var now = DateTime.UtcNow;
            context.ResidentAccessCredentials.Add(new ResidentAccessCredentialDTO
            {
                Id = Guid.NewGuid(),
                ResidentId = snapshot.ResidentId.Value,
                CredentialType = AccessCredentialTypeEnum.VehicleTag,
                Identifier = snapshot.TagIdentifier,
                IsActive = snapshot.IsActive,
                ValidFrom = now,
                ValidTo = now.AddYears(10),
                CreatedAt = now
            });
        }
        return RecycleRestoreResult.Ok("Veiculo restaurado.");
    }

    private async Task<RecycleRestoreResult> RestoreDeviceAsync(RecycleBinItemDTO item, CancellationToken cancellationToken)
    {
        var snapshot = Deserialize<DeviceSnapshot>(item.SnapshotJson);
        if (snapshot is null) return RecycleRestoreResult.Conflict("O snapshot do equipamento esta corrompido.");
        var duplicate = await context.Devices.AnyAsync(x =>
            x.LicenseId == item.LicenseId &&
            ((x.IPAddress == snapshot.IPAddress && x.Port == snapshot.Port) ||
             (!string.IsNullOrWhiteSpace(snapshot.SerialNumber) && x.SerialNumber == snapshot.SerialNumber) ||
             (!string.IsNullOrWhiteSpace(snapshot.MacAddress) && x.MACAddress == snapshot.MacAddress)), cancellationToken);
        if (duplicate) return RecycleRestoreResult.Conflict("Ja existe um equipamento com o mesmo IP, serial ou MAC.");

        context.Devices.Add(new AccessControlDeviceDTO
        {
            Id = item.EntityId,
            LicenseId = item.LicenseId,
            Name = snapshot.Name,
            IPAddress = snapshot.IPAddress,
            Port = snapshot.Port,
            Username = snapshot.Username,
            Password = snapshot.Password,
            MACAddress = snapshot.MacAddress,
            Model = snapshot.Model,
            SerialNumber = snapshot.SerialNumber,
            FirmwareVersion = snapshot.FirmwareVersion,
            Type = snapshot.Type,
            IsActive = false,
            Location = new LocationDTO { X = snapshot.LocationX, Y = snapshot.LocationY },
            CreatedAt = snapshot.CreatedAt,
            LastUpdatedAt = DateTime.UtcNow,
            LastHealthCheckAt = snapshot.LastHealthCheckAt,
            LastSeenAt = snapshot.LastSeenAt,
            LastResponseTimeMs = snapshot.LastResponseTimeMs,
            HealthMessage = "Restaurado como inativo. Execute um diagnostico antes de ativar.",
            CapacityJson = snapshot.CapacityJson,
            DiscoveredPortalsJson = snapshot.DiscoveredPortalsJson
        });
        return RecycleRestoreResult.Ok("Equipamento restaurado como inativo.");
    }

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public sealed record BlockSnapshot(string Name, DateTime CreatedAt, DateTime LastUpdatedAt);
    public sealed record UnitSnapshot(Guid BlockId, string Number, string Floor);
    public sealed record ResidentSnapshot(
        Guid UnitId,
        string Name,
        string Email,
        string Password,
        string PhoneNumber,
        string CommercialPhone,
        string CPF,
        string RG,
        string BirthDate,
        string ApartmentNumber,
        string ImageUrl,
        string Description,
        ResidentAccessTypeEnum AccessType,
        bool FirstAccess,
        bool NotifyAccess,
        bool IsActive,
        bool Temporary,
        DateTime Expire,
        DateTime LastAccess,
        DateTime CreatedAt,
        ResidentUnitRelationshipEnum Relationship,
        string LinkDescription,
        List<InvitationSnapshot> Invitations,
        List<RouteOverrideSnapshot> RouteOverrides);
    public sealed record VehicleSnapshot(
        Guid UnitId,
        Guid? ResidentId,
        string Plate,
        string Brand,
        string Model,
        string Color,
        string Type,
        string TagIdentifier,
        bool IsActive,
        DateTime CreatedAt,
        DateTime UpdatedAt);
    public sealed record DeviceSnapshot(
        string Name,
        string IPAddress,
        int Port,
        string Username,
        string Password,
        string? MacAddress,
        string Model,
        string? SerialNumber,
        string? FirmwareVersion,
        DeviceTypeEnum Type,
        bool IsActive,
        float LocationX,
        float LocationY,
        DateTime CreatedAt,
        DateTime LastUpdatedAt,
        DateTime? LastHealthCheckAt,
        DateTime? LastSeenAt,
        int? LastResponseTimeMs,
        string HealthMessage,
        string CapacityJson,
        string DiscoveredPortalsJson);
    public sealed record InvitationSnapshot(
        Guid Id,
        string Contact,
        RegistrationInviteChannelEnum Channel,
        RegistrationInviteStatusEnum Status,
        string TokenHash,
        string CreatedBy,
        int SendCount,
        DateTime SentAt,
        DateTime ExpiresAt,
        DateTime? OpenedAt,
        DateTime? CompletedAt,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public static InvitationSnapshot From(RegistrationInviteDTO value) => new(
            value.Id, value.Contact, value.Channel, value.Status, value.TokenHash, value.CreatedBy,
            value.SendCount, value.SentAt, value.ExpiresAt, value.OpenedAt, value.CompletedAt,
            value.CreatedAt, value.UpdatedAt);

        public RegistrationInviteDTO ToDto(Guid licenseId, Guid residentId) => new()
        {
            Id = Id,
            LicenseId = licenseId,
            ResidentId = residentId,
            Contact = Contact,
            Channel = Channel,
            Status = Status,
            TokenHash = TokenHash,
            CreatedBy = CreatedBy,
            SendCount = SendCount,
            SentAt = SentAt,
            ExpiresAt = ExpiresAt,
            OpenedAt = OpenedAt,
            CompletedAt = CompletedAt,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
    public sealed record RouteOverrideSnapshot(
        Guid Id,
        Guid AccessRouteId,
        AccessRouteOverrideModeEnum Mode,
        string Reason,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        public static RouteOverrideSnapshot From(AccessRouteResidentOverrideDTO value) =>
            new(value.Id, value.AccessRouteId, value.Mode, value.Reason, value.CreatedAt, value.UpdatedAt);

        public AccessRouteResidentOverrideDTO ToDto(Guid residentId) => new()
        {
            Id = Id,
            AccessRouteId = AccessRouteId,
            ResidentId = residentId,
            Mode = Mode,
            Reason = Reason,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}

public sealed record RecycleRestoreResult(
    bool Success,
    bool Found,
    string Message,
    Guid ItemId = default,
    Guid EntityId = default,
    string EntityType = "")
{
    public static RecycleRestoreResult Ok(string message) => new(true, true, message);
    public static RecycleRestoreResult NotFound() => new(false, false, "Item nao encontrado.");
    public static RecycleRestoreResult Conflict(string message) => new(false, true, message);
}

public sealed record RecyclePurgeResult(bool Success, bool Found);
