using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Domain.Enums.AccessControl;

namespace CondotifyAPI.Services.AccessControl;

public sealed record CredentialProvisionRequest(
    Guid CredentialId,
    string ResidentName,
    string Registration,
    AccessCredentialTypeEnum Type,
    string Identifier,
    string? ImageBase64,
    DateTime ValidFrom,
    DateTime ValidTo,
    bool IsActive,
    string? ExternalUserId = null,
    string? ExternalCredentialId = null,
    IReadOnlyList<AccessPortalAssignment>? Portals = null);

public sealed record AccessPortalAssignment(
    int PortalNumber,
    AccessRouteDirectionEnum Direction,
    string RouteName,
    int DaysOfWeekMask,
    TimeSpan StartTime,
    TimeSpan EndTime);

public sealed record CredentialOperationResult(
    bool Success,
    string? ExternalUserId = null,
    string? ExternalCredentialId = null,
    string? Message = null)
{
    public static CredentialOperationResult Ok(string? userId, string? credentialId, string? message = null) =>
        new(true, userId, credentialId, message);

    public static CredentialOperationResult Fail(string message) => new(false, Message: message);
}

public sealed record DeviceAccessEvent(
    string ExternalId,
    string Event,
    bool Authorized,
    DateTime OccurredAt,
    string? ExternalUserId = null,
    string? PersonName = null,
    string? Credential = null,
    string? Portal = null,
    string? Details = null);

public sealed record DevicePortalCapability(
    int Number,
    string Name,
    AccessRouteDirectionEnum Direction,
    bool Discovered = true);

public sealed record DeviceInspectionResult(
    bool Online,
    int? ResponseTimeMs,
    string Message,
    string? FirmwareVersion,
    string CapacityJson,
    IReadOnlyList<DevicePortalCapability> Portals)
{
    public static DeviceInspectionResult Unavailable(string message) =>
        new(false, null, message, null, "{}", []);
}

public static class AccessControlDeviceRegistration
{
    public static string FromResidentId(Guid residentId)
    {
        var bytes = residentId.ToByteArray();
        var value = BitConverter.ToUInt32(bytes, 0) & 0x7FFFFFFF;
        return Math.Max(1, value).ToString();
    }
}
